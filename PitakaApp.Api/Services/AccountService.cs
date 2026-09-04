using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class AccountService
{
    private readonly PitakaDbContext _context;

    public AccountService(PitakaDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Account>> GetAllForUser(User user) =>
        await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .OrderBy(a => a.Name)
            .ToListAsync();

    public async Task<Account?> GetByIdForUser(User user, int id) =>
        await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Account?> GetTrackedByIdForUserAsync(User user, int id) =>
        await _context.Accounts
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId));

    public async Task<Account> CreateAsync(User user, CreateAccountInput input)
    {
        var account = Account.Open(user.Id, input.Name, input.Type, input.InitialBalance);

        _context.Accounts.Add(account);

        await _context.SaveChangesAsync();
        return account; 
    }

    public async Task<Account> UpdateAsync(Account account, UpdateAccountInput input)
    {
        account.Name = input.Name;
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<Account> PatchActiveStatus(Account account, PatchAccountActiveInput input)
    {

        if (input.IsActive)
        {
            account.Activate();
        } else
        {
            account.Deactivate();
        }

        await _context.SaveChangesAsync();
        return account;
    }

    public async Task DeleteAsync(Account account)
    {
        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> HasTransactionHistoryAsync(int accountId) =>
        await _context.Transactions
            .AsNoTracking()
            .AnyAsync(t => t.AccountId == accountId || t.TransferToAccountId == accountId);

    public async Task<bool> HasGoalContributionsAsync(int accountId) =>
        await _context.GoalContributions
            .AsNoTracking()
            .AnyAsync(t => t.AccountId == accountId);
}