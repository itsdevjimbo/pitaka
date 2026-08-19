using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class GoalContributionService
{
    private readonly PitakaDbContext _context;

    private readonly AccountService _accountService;

    public GoalContributionService(
        PitakaDbContext context,
        AccountService accountService
    )
    {
        _context = context;
        _accountService = accountService;
    }
    
    public async Task<List<GoalContribution>> GetAllForUser(User user) =>
        await _context.GoalContributions
            .AsNoTracking()
            .Where(a => a.Goal.UserId == user.Id)
            .ToListAsync();

    public async Task<GoalContribution?> GetByIdForUser(User user, int id) =>
        await _context.GoalContributions
            .AsNoTracking()
            .Where(a => a.Id == id && a.Goal.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<GoalContribution?> GetTrackedByIdAsync(int id) =>
        await _context.GoalContributions
            .Include(gc => gc.Goal)
            .Where(gc => gc.Id == id)
            .FirstOrDefaultAsync();

    public async Task<GoalContribution> CreateAsync(Goal goal, Account account, CreateGoalContributionInput input)
    {
        var goalContribution = new GoalContribution
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            TransactionId = input.TransactionId,
            ContributionDate = input.ContributionDate,
            Amount = input.Amount,
            Note = input.Note
        };

        _context.Entry(account).State = EntityState.Modified;
        _context.GoalContributions.Add(goalContribution);

        await _context.SaveChangesAsync();
        return goalContribution; 
    }

    public async Task<bool> CanEarmarkTransaction(int accountId, int? transactionId)
    {
        if (transactionId is not int id)
        {
            return true;
        }
        
        return await _context.Transactions
            .AnyAsync(
                t => t.Id == id &&
                (
                    (t.Type == TransactionType.Income && t.AccountId== accountId) ||
                    (t.Type == TransactionType.Transfer && t.TransferToAccountId == accountId)
                )
            );
    }

    public async Task<bool> CanEarmarkAmount(Account account, decimal amount)
    {
        var totalContribution = await _context.GoalContributions
            .Where(gc => gc.AccountId == account.Id)
            .SumAsync(gc => gc.Amount);

        return totalContribution + amount <= account.CurrentBalance;
    }

    public async Task<GoalContribution> UpdateAsync(GoalContribution goalContribution, UpdateGoalContributionInput input)
    {
        goalContribution.ContributionDate = input.ContributionDate ?? goalContribution.ContributionDate;
        goalContribution.Note = input.Note;

        await _context.SaveChangesAsync();
        return goalContribution;
    }

    public async Task DeleteAsync(GoalContribution goalContribution)
    {
        _context.GoalContributions.Remove(goalContribution);
        await _context.SaveChangesAsync();
    }
}