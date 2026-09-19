using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public sealed class ContributionGuards(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<ContributionGuardSnapshot> CaptureAsync(
        int userId,
        int accountId,
        IReadOnlyCollection<int> goalIds,
        int? transactionId = null,
        CancellationToken cancellationToken = default
    )
    {
        var distinctGoalIds = goalIds.Distinct().Order().ToArray();

        // Capture every optimistic-concurrency token before reading any fact it protects.
        // Account is always first, then Goals in ID order, so all creation writers acquire
        // their guards consistently.
        var account = await _context.Accounts.SingleOrDefaultAsync(
            a => a.Id == accountId && a.UserId == userId,
            cancellationToken
        );
        var goals = await _context
            .Goals.Where(g => distinctGoalIds.Contains(g.Id) && g.UserId == userId)
            .OrderBy(g => g.Id)
            .ToListAsync(cancellationToken);

        var transaction = transactionId is int sourceTransactionId
            ? await _context
                .Transactions.AsNoTracking()
                .SingleOrDefaultAsync(
                    t => t.Id == sourceTransactionId && t.UserId == userId,
                    cancellationToken
                )
            : null;

        AccountHeadroomObservation? accountHeadroom = null;
        if (account is not null)
        {
            var earmarkedTotal = await _context
                .GoalContributions.Where(gc => gc.AccountId == account.Id)
                .SumAsync(gc => gc.Amount, cancellationToken);
            accountHeadroom = new AccountHeadroomObservation(account, earmarkedTotal);
        }

        TransactionCapacityObservation? transactionCapacity = null;
        IncomeEligibilityObservation? income = null;
        if (transaction is not null)
        {
            var linkedTotal = await _context
                .GoalContributions.Where(gc => gc.TransactionId == transaction.Id)
                .SumAsync(gc => gc.Amount, cancellationToken);
            transactionCapacity = new TransactionCapacityObservation(transaction, linkedTotal);

            if (account is not null)
            {
                income = new IncomeEligibilityObservation(transaction, account);
            }
        }

        var progressByGoal = await _context
            .GoalContributions.Where(gc => distinctGoalIds.Contains(gc.GoalId))
            .GroupBy(gc => gc.GoalId)
            .Select(group => new { GoalId = group.Key, Amount = group.Sum(gc => gc.Amount) })
            .ToDictionaryAsync(row => row.GoalId, row => row.Amount, cancellationToken);

        var goalObservations = goals
            .Select(goal => new GoalContributionObservation(
                goal,
                progressByGoal.GetValueOrDefault(goal.Id)
            ))
            .ToList();

        return new ContributionGuardSnapshot(
            accountHeadroom,
            transactionCapacity,
            income,
            goalObservations
        );
    }

    public void MarkConcurrencyGuardsModified(ContributionGuardSnapshot snapshot)
    {
        MarkConcurrencyGuardsModified(
            snapshot.AccountHeadroom?.Account,
            snapshot.Goals.Select(observation => observation.Goal)
        );
    }

    public void MarkConcurrencyGuardsModified(Account? account, IEnumerable<Goal> goals)
    {
        if (account is not null)
        {
            _context.Entry(account).State = EntityState.Modified;
        }

        foreach (var goal in goals.OrderBy(goal => goal.Id))
        {
            _context.Entry(goal).State = EntityState.Modified;
        }
    }
}

public sealed record ContributionGuardSnapshot(
    AccountHeadroomObservation? AccountHeadroom,
    TransactionCapacityObservation? TransactionCapacity,
    IncomeEligibilityObservation? IncomeEligibility,
    IReadOnlyList<GoalContributionObservation> Goals
);

public sealed record IncomeEligibilityObservation(Transaction Transaction, Account Account)
{
    public bool IsEligible =>
        Account.IsActive
        && Transaction.Type == TransactionType.Income
        && Transaction.AccountId == Account.Id
        && Transaction.UserId == Account.UserId;
}

public sealed record TransactionCapacityObservation(Transaction Transaction, decimal LinkedTotal)
{
    public decimal RemainingCapacity => Transaction.Amount - LinkedTotal;

    public bool CanAllocate(decimal amount) => amount <= RemainingCapacity;
}

public sealed record AccountHeadroomObservation(Account Account, decimal EarmarkedTotal)
{
    public decimal AvailableHeadroom => Account.CurrentBalance - EarmarkedTotal;

    public bool CanEarmark(decimal amount) => amount <= AvailableHeadroom;
}

public sealed record GoalContributionObservation(Goal Goal, decimal CurrentAmount)
{
    public bool IsEligibleForLinkedContribution => Goal.Status == GoalStatus.Active;

    public GoalOverrunObservation ObserveOverrun(decimal amount) =>
        new(CurrentAmount, Goal.TargetAmount, CurrentAmount + amount);
}

public sealed record GoalOverrunObservation(
    decimal CurrentAmount,
    decimal TargetAmount,
    decimal ProposedAmount
)
{
    public bool ExceedsTarget => ProposedAmount > TargetAmount;
}
