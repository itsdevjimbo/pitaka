using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Tests.Fixtures;

public enum SplitPhase
{
    BeforeInitialLookup,
    BeforeRecoveryLookup,
    BeforeWriteConnection,
    BeforeReservation,
    AfterReservation,
    BeforeGuards,
    AfterGuards,
    BeforeContributions,
    AfterContributions,
    BeforeResult,
    AfterResult,
    BeforeCommit,
    AfterCommit,
    AfterWriteDisposal,
    AfterAccountTotalReader,
}

// One scope owns every targeted operation in a race. Release every pause before joining any
// operation, including when an assertion or a wait fails partway through the scenario.
internal sealed class SplitFaultSchedule : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly List<Operation> operations = [];
    private readonly List<Pause> pauses = [];

    public Operation CreateOperation()
    {
        var operation = new Operation(this);
        operations.Add(operation);
        return operation;
    }

    internal static LinkedContributionSplitService CreateService(
        params IInterceptor[] interceptors
    ) =>
        new(
            new DbContextOptionsBuilder<PitakaDbContext>()
                .UseMySql(
                    PitakaWebApplicationFactory.TestConnectionString,
                    new MySqlServerVersion(new Version(8, 0, 0))
                )
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(interceptors)
                .Options
        );

    public async ValueTask DisposeAsync()
    {
        foreach (var pause in pauses)
        {
            pause.Release();
        }

        var errors = new List<Exception>();
        foreach (var operation in operations)
        {
            try
            {
                await operation.JoinAsync();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }
        // Verify outside interceptors: the production recovery path can catch injected failures
        // and even assertion exceptions. Missing/repeated triggers must still fail the test.
        foreach (var operation in operations)
        {
            errors.AddRange(operation.VerificationErrors());
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Split fault scheduling failed.", errors);
        }
    }

    public sealed class Pause
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task WaitUntilReachedAsync() => entered.Task.WaitAsync(Timeout);

        public void Release() => released.TrySetResult();

        internal async Task EnterAsync()
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(Timeout);
        }
    }

    public sealed class Operation(SplitFaultSchedule owner)
    {
        private readonly Dictionary<SplitPhase, Trigger> triggers = [];
        private readonly ConcurrentDictionary<SplitPhase, int> occurrences = new();
        private readonly ConcurrentQueue<Exception> errors = new();
        private readonly Dictionary<Guid, SplitPhase> saves = [];
        private readonly CancellationTokenSource cancellation = new();
        private Task<LinkedContributionSplitResult>? execution;
        private Guid? lookupContext;
        private Guid? writeContext;
        private DbConnection? writeConnection;

        public int Count(SplitPhase phase) => occurrences.GetValueOrDefault(phase);

        public void On(SplitPhase phase, Func<Task> action) => Configure(phase, new(action, null));

        public void Fail(SplitPhase phase, Exception failure) =>
            Configure(phase, new(() => Task.CompletedTask, failure));

        public Pause PauseAt(SplitPhase phase, Exception? failureAfterRelease = null)
        {
            var pause = new Pause();
            Configure(phase, new(pause.EnterAsync, failureAfterRelease));
            owner.pauses.Add(pause);
            return pause;
        }

        private void Configure(SplitPhase phase, Trigger trigger)
        {
            if (execution is not null)
            {
                throw new InvalidOperationException(
                    "Configure phases before starting the operation."
                );
            }
            triggers.Add(phase, trigger);
        }

        public Task<LinkedContributionSplitResult> ExecuteAsync(
            int userId,
            int transactionId,
            Guid key,
            LinkedContributionSplitInput input
        )
        {
            if (execution is not null)
            {
                throw new InvalidOperationException(
                    "Each scheduled operation executes once; use a fresh operation for retries."
                );
            }
            cancellation.CancelAfter(TimeSpan.FromSeconds(30));
            execution = CreateService(
                    new Saves(this),
                    new Connections(this),
                    new Commands(this),
                    new Transactions(this)
                )
                .ExecuteAsync(userId, transactionId, key, input, cancellation.Token);
            return execution;
        }

        internal async Task JoinAsync()
        {
            try
            {
                if (execution is null)
                {
                    return;
                }
                try
                {
                    await execution.WaitAsync(Timeout);
                }
                catch (Exception) when (execution.IsCompleted)
                {
                    // The test owns the result (including expected injected exceptions).
                    // Awaiting here also observes an exception if an earlier assertion aborted it.
                }
                catch (TimeoutException)
                {
                    await cancellation.CancelAsync();
                    try
                    {
                        await execution.WaitAsync(Timeout);
                    }
                    catch (Exception) when (execution.IsCompleted) { }
                    throw new TimeoutException("A scheduled split did not finish during cleanup.");
                }
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        internal IEnumerable<Exception> VerificationErrors()
        {
            foreach (var phase in triggers.Keys)
            {
                if (Count(phase) != 1)
                {
                    yield return new InvalidOperationException(
                        $"Expected {phase} exactly once; observed {Count(phase)}."
                    );
                }
            }
            foreach (var error in errors)
            {
                yield return error;
            }
        }

        private async Task ReachAsync(SplitPhase phase)
        {
            var count = occurrences.AddOrUpdate(phase, 1, (_, previous) => previous + 1);
            if (!triggers.TryGetValue(phase, out var trigger) || count != 1)
            {
                return;
            }
            try
            {
                await trigger.Action();
            }
            catch (Exception exception)
            {
                errors.Enqueue(exception);
                throw;
            }
            if (trigger.Failure is not null)
            {
                throw trigger.Failure;
            }
        }

        private bool IsWrite(DbContext? context) =>
            context?.ContextId.InstanceId == writeContext && writeContext is not null;

        private sealed record Trigger(Func<Task> Action, Exception? Failure);

        private sealed class Saves(Operation operation) : SaveChangesInterceptor
        {
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default
            )
            {
                var context = eventData.Context!;
                context.ChangeTracker.DetectChanges();
                var entries = context.ChangeTracker.Entries().ToList();
                var phase =
                    entries.Any(e =>
                        e.Entity is LinkedContributionOperation && e.State == EntityState.Added
                    )
                        ? SplitPhase.BeforeReservation
                    : entries.Any(e => e.Entity is GoalContribution && e.State == EntityState.Added)
                        ? SplitPhase.BeforeContributions
                    : entries.Any(e =>
                        e.Entity is LinkedContributionOperation && e.State == EntityState.Modified
                    )
                        ? SplitPhase.BeforeResult
                    : entries.Any(e =>
                        e.Entity is Account or Goal && e.State == EntityState.Modified
                    )
                        ? SplitPhase.BeforeGuards
                    : throw new InvalidOperationException("Unrecognized split save phase.");
                if (phase == SplitPhase.BeforeReservation)
                {
                    operation.writeConnection = context.Database.GetDbConnection();
                }
                // SavedChanges sees accepted entities, so remember their meaning before saving.
                operation.saves[context.ContextId.InstanceId] = phase;
                await operation.ReachAsync(phase);
                return result;
            }

            public override async ValueTask<int> SavedChangesAsync(
                SaveChangesCompletedEventData eventData,
                int result,
                CancellationToken cancellationToken = default
            )
            {
                var phase = operation.saves[eventData.Context!.ContextId.InstanceId] switch
                {
                    SplitPhase.BeforeReservation => SplitPhase.AfterReservation,
                    SplitPhase.BeforeGuards => SplitPhase.AfterGuards,
                    SplitPhase.BeforeContributions => SplitPhase.AfterContributions,
                    SplitPhase.BeforeResult => SplitPhase.AfterResult,
                    _ => throw new InvalidOperationException("Unrecognized completed split save."),
                };
                await operation.ReachAsync(phase);
                return result;
            }
        }

        private sealed class Connections(Operation operation) : DbConnectionInterceptor
        {
            public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection,
                ConnectionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default
            )
            {
                var id = eventData.Context!.ContextId.InstanceId;
                operation.lookupContext ??= id;
                // Before opening the write connection there are no tracked reservation entities.
                // The lookup-then-fresh-write-context ordering is deliberately localized here.
                if (id != operation.lookupContext)
                {
                    operation.writeContext ??= id;
                    if (id == operation.writeContext)
                    {
                        await operation.ReachAsync(SplitPhase.BeforeWriteConnection);
                    }
                }
                return result;
            }

            public override Task ConnectionDisposedAsync(
                DbConnection connection,
                ConnectionEndEventData eventData
            ) =>
                ReferenceEquals(connection, operation.writeConnection)
                    ? operation.ReachAsync(SplitPhase.AfterWriteDisposal)
                    : Task.CompletedTask;
        }

        private sealed class Commands(Operation operation) : DbCommandInterceptor
        {
            public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default
            )
            {
                if (
                    command
                        .CommandText.TrimStart()
                        .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    && command.CommandText.Contains("`linked_contribution_operations`")
                )
                {
                    await operation.ReachAsync(
                        eventData.Context!.ContextId.InstanceId == operation.lookupContext
                            ? SplitPhase.BeforeInitialLookup
                            : SplitPhase.BeforeRecoveryLookup
                    );
                }
                return result;
            }

            public override async ValueTask<DbDataReader> ReaderExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                DbDataReader result,
                CancellationToken cancellationToken = default
            )
            {
                // EF has no semantic aggregate-read event. Keep provider SQL knowledge here,
                // and preserve the original timing: reader returned, not result materialized.
                var sql = command.CommandText;
                if (
                    operation.IsWrite(eventData.Context)
                    && sql.Contains("SUM(")
                    && sql.Contains("`goal_contributions`")
                    && sql.Contains("`account_id` =")
                    && !sql.Contains("GROUP BY")
                )
                {
                    await operation.ReachAsync(SplitPhase.AfterAccountTotalReader);
                }
                return result;
            }
        }

        private sealed class Transactions(Operation operation) : DbTransactionInterceptor
        {
            public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default
            )
            {
                if (operation.IsWrite(eventData.Context))
                {
                    await operation.ReachAsync(SplitPhase.BeforeCommit);
                }
                return result;
            }

            public override Task TransactionCommittedAsync(
                DbTransaction transaction,
                TransactionEndEventData eventData,
                CancellationToken cancellationToken = default
            ) =>
                operation.IsWrite(eventData.Context)
                    ? operation.ReachAsync(SplitPhase.AfterCommit)
                    : Task.CompletedTask;
        }
    }
}
