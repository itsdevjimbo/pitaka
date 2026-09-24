using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class GoalContributionService(PitakaDbContext context, ContributionGuards contributionGuards)
{
    private readonly PitakaDbContext _context = context;
    private readonly ContributionGuards _contributionGuards = contributionGuards;

    public async Task<List<GoalContribution>> GetAllForUser(User user) =>
        await _context
            .GoalContributions.AsNoTracking()
            .Where(a => a.Goal.UserId == user.Id)
            .ToListAsync();

    public async Task<List<GoalContribution>> GetAllForGoal(Goal goal) =>
        await _context
            .GoalContributions.AsNoTracking()
            .Where(a => a.GoalId == goal.Id)
            .ToListAsync();

    public async Task<GoalContribution?> GetByIdForUser(User user, int id) =>
        await _context
            .GoalContributions.AsNoTracking()
            .Where(a => a.Id == id && a.Goal.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<GoalContribution?> GetTrackedByIdForUserAsync(
        int userId,
        int id,
        CancellationToken cancellationToken
    ) =>
        await _context
            .GoalContributions.Include(gc => gc.Goal)
            .Where(gc => gc.Id == id && gc.Goal.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<GoalContribution> CreateAsync(
        Goal goal,
        Account account,
        CreateGoalContributionInput input
    )
    {
        var goalContribution = new GoalContribution
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            TransactionId = input.TransactionId,
            ContributionDate = input.ContributionDate,
            Amount = input.Amount,
            Note = input.Note,
        };

        _contributionGuards.MarkConcurrencyGuardsModified(account, [goal]);
        _context.GoalContributions.Add(goalContribution);

        await _context.SaveChangesAsync();
        return goalContribution;
    }

    public async Task<bool> CanEarmarkAmount(Account account, decimal amount)
    {
        var totalContribution = await _context
            .GoalContributions.Where(gc => gc.AccountId == account.Id)
            .SumAsync(gc => gc.Amount);

        return totalContribution + amount <= account.CurrentBalance;
    }

    public async Task<GoalContribution> UpdateAsync(
        GoalContribution goalContribution,
        UpdateGoalContributionInput input,
        CancellationToken cancellationToken = default
    )
    {
        goalContribution.Note = input.Note;

        await _context.SaveChangesAsync(cancellationToken);
        return goalContribution;
    }

    // A single database statement makes a concurrent deletion indistinguishable from an
    // already-absent row, while the ownership predicate avoids disclosing another User's row.
    public async Task<bool> DeleteForUserAsync(int userId, int id) =>
        await _context
            .GoalContributions.Where(gc => gc.Id == id && gc.Goal.UserId == userId)
            .ExecuteDeleteAsync() == 1;

    public async Task DeleteAsync(GoalContribution goalContribution)
    {
        _context.GoalContributions.Remove(goalContribution);
        await _context.SaveChangesAsync();
    }
}
