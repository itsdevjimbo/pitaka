using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class UpdateAccountBalance(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<Account> ApplyTransaction(Transaction transaction)
    {
        var accounts = await GetTrackedAccountsOrThrowAsync(
            transaction.AccountId,
            transaction.TransferToAccountId
        );
        var account = accounts[transaction.AccountId];

        switch (transaction.Type)
        {
            case TransactionType.Income:
                account.Increase(transaction.Amount);
                break;
            case TransactionType.Expense:
                account.Decrease(transaction.Amount);
                break;
            case TransactionType.Transfer:
                accounts[transaction.TransferToAccountId!.Value].Increase(transaction.Amount);
                account.Decrease(transaction.Amount);
                break;
            default:
                throw new InvalidOperationException($"Invalid type: {transaction.Type}");
        }

        return account;
    }

    public async Task<Account> ReverseTransaction(
        Transaction transaction,
        CancellationToken cancellationToken = default
    )
    {
        var accounts = await GetTrackedAccountsOrThrowAsync(
            transaction.AccountId,
            transaction.TransferToAccountId,
            cancellationToken
        );
        var account = accounts[transaction.AccountId];

        switch (transaction.Type)
        {
            case TransactionType.Income:
                account.Decrease(transaction.Amount);
                break;
            case TransactionType.Expense:
                account.Increase(transaction.Amount);
                break;
            case TransactionType.Transfer:
                if (transaction.TransferToAccountId is int destinationAccountId)
                {
                    accounts[destinationAccountId].Decrease(transaction.Amount);
                }
                account.Increase(transaction.Amount);
                break;
            default:
                throw new InvalidOperationException($"Invalid type: {transaction.Type}");
        }

        return account;
    }

    private async Task<Dictionary<int, Account>> GetTrackedAccountsOrThrowAsync(
        int sourceAccountId,
        int? destinationAccountId,
        CancellationToken cancellationToken = default
    )
    {
        var accountIds = new[] { sourceAccountId, destinationAccountId }
            .OfType<int>()
            .Distinct()
            .Order()
            .ToArray();
        var accounts = await _context
            .Accounts.Where(account => accountIds.Contains(account.Id))
            .OrderBy(account => account.Id)
            .ToDictionaryAsync(account => account.Id, cancellationToken);

        foreach (var accountId in accountIds)
        {
            if (!accounts.ContainsKey(accountId))
            {
                throw new InvalidOperationException($"Account {accountId} not found.");
            }
        }

        return accounts;
    }
}
