using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class UpdateAccountBalance
{
    private readonly PitakaDbContext _context;
    public UpdateAccountBalance(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<Account> ApplyTransaction(Transaction transaction)
    {
        var account = await GetTrackedAccountOrThrowAsync(transaction.AccountId);

        if (account == null)
        {
            throw new InvalidOperationException($"Account {transaction.AccountId} not found.");
        }

        switch (transaction.Type)
        {
            case TransactionType.Income:
                account.Increase(transaction.Amount);
                break;
            case TransactionType.Expense:
                account.Decrease(transaction.Amount);
                break;
            case TransactionType.Transfer:
                await TransferToAccount(transaction.Amount, transaction.TransferToAccountId);
                account.Decrease(transaction.Amount);
                break;
            default:
                throw new InvalidOperationException($"Invalid type: {transaction.Type}");
        }
        
        return account;
    }

    public async Task<Account> ReverseTransaction(Transaction transaction)
    {
        var account = await GetTrackedAccountOrThrowAsync(transaction.AccountId);

        if (account == null)
        {
            throw new InvalidOperationException($"Account {transaction.AccountId} not found.");
        }

        switch (transaction.Type)
        {
            case TransactionType.Income:
                account.Decrease(transaction.Amount);
                break;
            case TransactionType.Expense:
                account.Increase(transaction.Amount);
                break;
            case TransactionType.Transfer:
                await ReverseTransfer(transaction.Amount, transaction.TransferToAccountId);
                account.Increase(transaction.Amount);
                break;
            default:
                throw new InvalidOperationException($"Invalid type: {transaction.Type}");
        }
        
        return account;
    }

    private async Task<Account> TransferToAccount(decimal amount, int? accountId)
    {
        var account = await GetTrackedAccountOrThrowAsync(accountId);

        if (account == null)
        {
            throw new InvalidOperationException($"Account {accountId} not found.");
        }

        account.Increase(amount);
        return account;
    }

    private async Task<Account> ReverseTransfer(decimal amount, int? accountId)
    {
        var account = await GetTrackedAccountOrThrowAsync(accountId);

        account.Decrease(amount);

        return account;
    }

    private async Task<Account> GetTrackedAccountOrThrowAsync(int? accountId)
{
    var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
    return account ?? throw new InvalidOperationException($"Account {accountId} not found.");
}
}