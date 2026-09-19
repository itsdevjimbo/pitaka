using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

// Each execution owns its context: unrelated tracked changes cannot join the monetary write.
public sealed class LinkedContributionSplitService(DbContextOptions<PitakaDbContext> options)
{
    public async Task<LinkedContributionSplitResult> ExecuteAsync(
        int userId,
        int transactionId,
        Guid key,
        LinkedContributionSplitInput input,
        CancellationToken cancellationToken = default
    )
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return new SplitInvalid(errors);
        }
        // userId is supplied by the authenticated caller, never bound from request data.
        var normalizedKey = key.ToString("D");
        var fingerprint = Fingerprint(transactionId, input);
        using var arbitration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        arbitration.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var previous = await ReadResultAsync(
                userId,
                normalizedKey,
                fingerprint,
                arbitration.Token
            );
            if (previous is not null)
            {
                return previous;
            }
        }
        catch (Exception exception)
            when (exception is DbException or OperationCanceledException or IOException)
        {
            return new SplitOutcomeUnknown();
        }
        var attempt = new WriteAttempt();
        try
        {
            return await ExecuteNewAsync(
                userId,
                transactionId,
                normalizedKey,
                fingerprint,
                input,
                attempt,
                arbitration.Token,
                cancellationToken
            );
        }
        catch (Exception) when (attempt.CommitAttempted)
        {
            // The connection may have failed after MySQL committed. Disposal also runs before
            // this recovery, and its failure must not turn an uncertain commit into a refusal.
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await RecoverAsync(userId, normalizedKey, fingerprint, recovery.Token);
        }
        catch (ReservationCollision)
        {
            return await RecoverAsync(userId, normalizedKey, fingerprint, arbitration.Token);
        }
    }

    private async Task<LinkedContributionSplitResult?> ReadResultAsync(
        int userId,
        string key,
        string fingerprint,
        CancellationToken cancellationToken
    )
    {
        await using var context = new PitakaDbContext(options);
        var previous = await context
            .LinkedContributionOperations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.UserId == userId && o.Key == key, cancellationToken);
        if (previous is null)
        {
            return null;
        }

        if (previous.Fingerprint != fingerprint)
        {
            return new SplitIdempotencyMismatch(Guid.Parse(key));
        }

        return new SplitSucceeded(previous.ResponseBody!, previous.StatusCode!.Value);
    }

    private async Task<LinkedContributionSplitResult> ExecuteNewAsync(
        int userId,
        int transactionId,
        string normalizedKey,
        string fingerprint,
        LinkedContributionSplitInput input,
        WriteAttempt attempt,
        CancellationToken arbitrationToken,
        CancellationToken cancellationToken
    )
    {
        await using var context = new PitakaDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            arbitrationToken
        );
        var operation = new LinkedContributionOperation
        {
            UserId = userId,
            Key = normalizedKey,
            Fingerprint = fingerprint,
        };
        context.Add(operation);
        try
        {
            await context.SaveChangesAsync(arbitrationToken);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException || IsContention(exception))
        {
            throw new ReservationCollision();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is MySqlException { Number: 1062 })
        {
            // This save contains only the reservation, so the collision cannot come from a Contribution.
            throw new ReservationCollision();
        }
        int? accountId = null;
        try
        {
            var source = await context
                .Transactions.AsNoTracking()
                .SingleOrDefaultAsync(
                    t => t.Id == transactionId && t.UserId == userId,
                    cancellationToken
                );
            if (source is null)
            {
                return new SplitRefused([
                    new(
                        "transaction_missing",
                        new Dictionary<string, object?> { ["transactionId"] = transactionId }
                    ),
                ]);
            }

            accountId = source.AccountId;
            var guards = new ContributionGuards(context);
            var snapshot = await guards.CaptureAsync(
                userId,
                source.AccountId,
                [.. input.Contributions.Select(r => r.GoalId)],
                transactionId,
                cancellationToken
            );
            if (snapshot.AccountHeadroom is null)
            {
                return new SplitRefused([
                    new(
                        "transaction_missing",
                        new Dictionary<string, object?> { ["transactionId"] = transactionId }
                    ),
                ]);
            }

            var missingGoals = input
                .Contributions.Select((row, index) => (row, index))
                .Where(x => snapshot.Goals.All(g => g.Goal.Id != x.row.GoalId))
                .ToDictionary(
                    x => $"contributions[{x.index}].goalId",
                    _ => new[] { "Goal is unavailable." }
                );
            if (missingGoals.Count > 0)
            {
                return new SplitInvalid(missingGoals);
            }

            var failures = Evaluate(snapshot, input);
            guards.MarkConcurrencyGuardsModified(snapshot);
            // Verify the observations even on refusal; these guard writes are rolled back.
            await context.SaveChangesAsync(cancellationToken);
            if (failures.Count > 0)
            {
                return new SplitRefused(failures);
            }

            var contributions = input
                .Contributions.Select(row => new GoalContribution
                {
                    GoalId = row.GoalId,
                    AccountId = source.AccountId,
                    TransactionId = transactionId,
                    Amount = row.Amount,
                    Note = row.Note,
                    ContributionDate = input.ContributionDate,
                })
                .ToList();
            context.GoalContributions.AddRange(contributions);
            await context.SaveChangesAsync(cancellationToken);
            var total = contributions.Sum(c => c.Amount);
            var account = snapshot.AccountHeadroom!;
            var capacity = snapshot.TransactionCapacity!;
            var response = new SplitSuccessSnapshot(
                transactionId,
                source.Amount,
                capacity.LinkedTotal + total,
                capacity.RemainingCapacity - total,
                new(
                    account.Account.Id,
                    account.Account.Name,
                    account.Account.CurrentBalance,
                    account.EarmarkedTotal + total,
                    account.AvailableHeadroom - total,
                    account.Account.IsActive
                ),
                [
                    .. contributions.Select(c => new SplitContributionSnapshot(
                        c.Id,
                        c.GoalId,
                        c.AccountId,
                        transactionId,
                        c.Amount,
                        c.ContributionDate,
                        c.Note
                    )),
                ]
            );
            var success = SplitSucceeded.FromSnapshot(response);
            operation.StatusCode = success.StatusCode;
            operation.ResponseBody = success.ResponseBody;
            await context.SaveChangesAsync(cancellationToken);
            attempt.CommitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            return success;
        }
        catch (Exception exception)
            when (!attempt.CommitAttempted
                && (exception is DbUpdateConcurrencyException || IsContention(exception))
            )
        {
            return new SplitRefused([
                new(
                    "concurrent_state_changed",
                    new Dictionary<string, object?>
                    {
                        ["transactionId"] = transactionId,
                        ["accountId"] = accountId,
                        ["goalIds"] = input.Contributions.Select(r => r.GoalId).ToArray(),
                    }
                ),
            ]);
        }
    }

    private async Task<LinkedContributionSplitResult> RecoverAsync(
        int userId,
        string key,
        string fingerprint,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await ReadResultAsync(userId, key, fingerprint, cancellationToken)
                ?? new SplitOutcomeUnknown();
        }
        catch (Exception exception)
            when (exception
                    is DbException
                        or DbUpdateException
                        or OperationCanceledException
                        or IOException
            )
        {
            return new SplitOutcomeUnknown();
        }
    }

    private static bool IsContention(Exception exception) =>
        (exception as MySqlException ?? exception.InnerException as MySqlException)?.Number
            is 1205
                or 1213;

    private sealed class WriteAttempt
    {
        public bool CommitAttempted { get; set; }
    }

    private sealed class ReservationCollision : Exception;

    private static Dictionary<string, string[]> Validate(LinkedContributionSplitInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Contributions.Count == 0)
        {
            errors["contributions"] = ["At least one Contribution is required."];
        }

        var duplicates = input
            .Contributions.GroupBy(r => r.GoalId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        for (var index = 0; index < input.Contributions.Count; index++)
        {
            var row = input.Contributions[index];
            if (
                row.Amount < 0.01m
                || row.Amount > 999999999999.99m
                || decimal.Round(row.Amount, 2) != row.Amount
            )
            {
                errors[$"contributions[{index}].amount"] =
                [
                    "Amount must be positive, cent-precise, and fit decimal(14,2).",
                ];
            }

            if (duplicates.Contains(row.GoalId))
            {
                errors[$"contributions[{index}].goalId"] = ["Each Goal may appear only once."];
            }
        }
        return errors;
    }

    private static List<SplitFailure> Evaluate(
        ContributionGuardSnapshot snapshot,
        LinkedContributionSplitInput input
    )
    {
        var failures = new List<SplitFailure>();
        var account = snapshot.AccountHeadroom!;
        var capacity = snapshot.TransactionCapacity!;
        var total = input.Contributions.Sum(row => row.Amount);
        if (!account.Account.IsActive)
        {
            failures.Add(
                new(
                    "account_inactive",
                    new Dictionary<string, object?>
                    {
                        ["accountId"] = account.Account.Id,
                        ["accountName"] = account.Account.Name,
                    }
                )
            );
        }

        if (capacity.Transaction.Type != TransactionType.Income)
        {
            failures.Add(
                new(
                    "transaction_ineligible",
                    new Dictionary<string, object?>
                    {
                        ["transactionId"] = capacity.Transaction.Id,
                        ["direction"] = capacity.Transaction.Type.ToString(),
                        ["accountId"] = account.Account.Id,
                        ["accountName"] = account.Account.Name,
                    }
                )
            );
        }

        if (!capacity.CanAllocate(total))
        {
            failures.Add(
                new(
                    "transaction_capacity_exceeded",
                    new Dictionary<string, object?>
                    {
                        ["transactionId"] = capacity.Transaction.Id,
                        ["transactionAmount"] = capacity.Transaction.Amount,
                        ["linkedTotal"] = capacity.LinkedTotal,
                        ["remainingCapacity"] = capacity.RemainingCapacity,
                    }
                )
            );
        }

        if (!account.CanEarmark(total))
        {
            failures.Add(
                new(
                    "account_headroom_exceeded",
                    new Dictionary<string, object?>
                    {
                        ["accountId"] = account.Account.Id,
                        ["accountName"] = account.Account.Name,
                        ["currentBalance"] = account.Account.CurrentBalance,
                        ["earmarkedTotal"] = account.EarmarkedTotal,
                        ["availableHeadroom"] = account.AvailableHeadroom,
                    }
                )
            );
        }

        for (var index = 0; index < input.Contributions.Count; index++)
        {
            var row = input.Contributions[index];
            var goal = snapshot.Goals.Single(g => g.Goal.Id == row.GoalId);
            if (!goal.IsEligibleForLinkedContribution)
            {
                failures.Add(
                    new(
                        "goal_inactive",
                        new Dictionary<string, object?>
                        {
                            ["goalName"] = goal.Goal.Name,
                            ["currentState"] = goal.Goal.Status.ToString(),
                        },
                        index,
                        row.GoalId
                    )
                );
            }

            var overrun = goal.ObserveOverrun(row.Amount);
            if (overrun.ExceedsTarget && !row.AcknowledgeTargetOverrun)
            {
                failures.Add(
                    new(
                        "target_overrun_acknowledgement_required",
                        new Dictionary<string, object?>
                        {
                            ["currentProgress"] = overrun.CurrentAmount,
                            ["target"] = overrun.TargetAmount,
                            ["proposedProgress"] = overrun.ProposedAmount,
                        },
                        index,
                        row.GoalId
                    )
                );
            }
        }
        return failures;
    }

    private static string Fingerprint(int transactionId, LinkedContributionSplitInput input) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new
                        {
                            Operation = "transaction-linked-contribution-split",
                            TransactionId = transactionId,
                            input.ContributionDate,
                            Rows = input.Contributions.Select(row => new
                            {
                                row.GoalId,
                                Amount = row.Amount.ToString("G29", CultureInfo.InvariantCulture),
                                row.Note,
                                row.AcknowledgeTargetOverrun,
                            }),
                        }
                    )
                )
            )
        );
}
