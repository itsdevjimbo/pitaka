using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class AccountService(
    PitakaDbContext context,
    CheckUserScopedNameExists checkUserScopedNameExists
)
{
    private readonly PitakaDbContext _context = context;
    private readonly CheckUserScopedNameExists _checkUserScopedNameExists =
        checkUserScopedNameExists;

    public async Task<List<Account>> GetAllForUser(User user, AccountQueryInput input)
    {
        var query = _context.Accounts.AsNoTracking().Where(a => a.UserId == user.Id);

        if (input.Type is AccountType type)
        {
            query = query.Where(a => a.Type == type);
        }

        if (input.IsActive is bool isActive)
        {
            query = query.Where(a => a.IsActive == isActive);
        }

        return await query.OrderBy(a => a.Name).ToListAsync();
    }

    public async Task<Account?> GetByIdForUserAsync(
        User user,
        int id,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .Accounts.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Account?> GetTrackedByIdForUserAsync(
        User user,
        int id,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .Accounts.Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> NameExistsForUserAsync(
        int userId,
        string name,
        int? excludeId = null,
        CancellationToken cancellationToken = default
    ) =>
        _checkUserScopedNameExists.ExecuteAsync(
            _context.Accounts,
            userId,
            name,
            account => account.UserId,
            account => account.Name,
            account => account.Id,
            excludeId,
            cancellationToken
        );

    public async Task<Account> CreateAsync(
        User user,
        CreateAccountInput input,
        CancellationToken cancellationToken = default
    )
    {
        var account = Account.Open(user.Id, input.Name, input.Type, input.InitialBalance);

        _context.Accounts.Add(account);

        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<Account> UpdateAsync(
        Account account,
        UpdateAccountInput input,
        CancellationToken cancellationToken = default
    )
    {
        account.Name = input.Name;
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<Account> PatchActiveStatus(Account account, PatchAccountActiveInput input)
    {
        if (input.IsActive)
        {
            account.Activate();
        }
        else
        {
            account.Deactivate();
        }

        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<AccountDeletionResult> DeleteAsync(
        int userId,
        int accountId,
        CancellationToken cancellationToken
    )
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(
            a => a.Id == accountId && a.UserId == userId,
            cancellationToken
        );

        if (account is null)
        {
            return AccountDeletionResult.NotFound;
        }

        if (await HasTransactionHistoryAsync(account.Id, cancellationToken))
        {
            return AccountDeletionResult.HasTransactionHistory;
        }

        if (await HasGoalContributionsAsync(account.Id, cancellationToken))
        {
            return AccountDeletionResult.HasGoalContributions;
        }

        if (await HasGeneratedRecurringTransactionsAsync(account.Id, cancellationToken))
        {
            return AccountDeletionResult.HasGeneratedRecurringTransactions;
        }

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync(cancellationToken);
        return AccountDeletionResult.Deleted;
    }

    private async Task<bool> HasTransactionHistoryAsync(
        int accountId,
        CancellationToken cancellationToken
    ) =>
        await _context
            .Transactions.AsNoTracking()
            .AnyAsync(
                t => t.AccountId == accountId || t.TransferToAccountId == accountId,
                cancellationToken
            );

    private async Task<bool> HasGoalContributionsAsync(
        int accountId,
        CancellationToken cancellationToken
    ) =>
        await _context
            .GoalContributions.AsNoTracking()
            .AnyAsync(t => t.AccountId == accountId, cancellationToken);

    private async Task<bool> HasGeneratedRecurringTransactionsAsync(
        int accountId,
        CancellationToken cancellationToken
    ) =>
        await _context
            .RecurringTransactions.AsNoTracking()
            .AnyAsync(
                rt => rt.AccountId == accountId && rt.HasGeneratedTransactions,
                cancellationToken
            );
}
