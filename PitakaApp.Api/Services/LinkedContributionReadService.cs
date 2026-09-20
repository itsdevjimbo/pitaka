using System.Data;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public sealed class LinkedContributionReadService(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<LinkedContributionSnapshot?> GetForTransactionAsync(
        int userId,
        int transactionId,
        CancellationToken cancellationToken = default
    )
    {
        await using var snapshot = await _context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken
        );

        var transaction = await _context
            .Transactions.AsNoTracking()
            .Include(t => t.Account)
            .SingleOrDefaultAsync(
                t => t.Id == transactionId && t.UserId == userId,
                cancellationToken
            );

        if (transaction is null)
        {
            await snapshot.RollbackAsync(cancellationToken);
            return null;
        }

        var linkedContributions = await _context
            .GoalContributions.AsNoTracking()
            .Include(gc => gc.Goal)
            .Where(gc => gc.TransactionId == transaction.Id)
            .OrderBy(gc => gc.Id)
            .ToListAsync(cancellationToken);
        var earmarkedTotal = await _context
            .GoalContributions.AsNoTracking()
            .Where(gc => gc.AccountId == transaction.AccountId)
            .SumAsync(gc => gc.Amount, cancellationToken);

        await snapshot.CommitAsync(cancellationToken);

        return new LinkedContributionSnapshot(transaction, earmarkedTotal, linkedContributions);
    }
}

public sealed record LinkedContributionSnapshot(
    Transaction Transaction,
    decimal EarmarkedTotal,
    IReadOnlyList<GoalContribution> LinkedContributions
)
{
    public decimal LinkedTotal => LinkedContributions.Sum(contribution => contribution.Amount);
}
