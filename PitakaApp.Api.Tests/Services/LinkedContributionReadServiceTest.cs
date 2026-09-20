using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public sealed class LinkedContributionReadServiceTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task GetForTransactionAsync_InterleavedWritesDoNotMixSnapshotStates()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 1000);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);
        var transaction = await TransactionFactory.CreateAsync(
            seedContext,
            user.Id,
            account.Id,
            amount: 300
        );
        var originalLinked = await GoalContributionFactory.CreateAsync(
            seedContext,
            goal.Id,
            account.Id,
            transaction.Id,
            amount: 100
        );
        var ordinary = await GoalContributionFactory.CreateAsync(
            seedContext,
            goal.Id,
            account.Id,
            amount: 200
        );
        seedContext.ChangeTracker.Clear();

        var pause = new PauseAfterTransactionRead();
        var baseOptions = seedScope.ServiceProvider.GetRequiredService<
            DbContextOptions<PitakaDbContext>
        >();
        var readOptions = new DbContextOptionsBuilder<PitakaDbContext>(baseOptions)
            .AddInterceptors(pause)
            .Options;
        await using var readContext = new PitakaDbContext(readOptions);
        var readTask = new LinkedContributionReadService(readContext).GetForTransactionAsync(
            user.Id,
            transaction.Id
        );

        await pause.WaitUntilReachedAsync();
        try
        {
            using var writeScope = factory.Services.CreateScope();
            var writeContext = writeScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
            var currentAccount = (await writeContext.Accounts.FindAsync(account.Id))!;
            currentAccount.Decrease(100);
            writeContext.GoalContributions.Add(
                GoalContributionFactory.Make(goal.Id, account.Id, transaction.Id, amount: 50)
            );
            writeContext.GoalContributions.Remove(
                (await writeContext.GoalContributions.FindAsync(ordinary.Id))!
            );
            await writeContext.SaveChangesAsync();
        }
        finally
        {
            pause.Release();
        }

        var snapshot = await readTask;

        Assert.NotNull(snapshot);
        Assert.Equal(1000, snapshot.Transaction.Account.CurrentBalance);
        Assert.Equal(300, snapshot.EarmarkedTotal);
        Assert.Equal(100, snapshot.LinkedTotal);
        Assert.Collection(
            snapshot.LinkedContributions,
            contribution => Assert.Equal(originalLinked.Id, contribution.Id)
        );
    }

    private sealed class PauseAfterTransactionRead : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _hasPaused;

        public Task WaitUntilReachedAsync() => _reached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _released.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains("FROM `transactions`", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _hasPaused, 1) == 0
            )
            {
                _reached.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
