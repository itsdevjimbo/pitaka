using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class TransactionFactory
{
    public static Transaction Make(
        int userId, 
        int accountId, 
        TransactionType type = TransactionType.Income,
        decimal amount = 100,
        int? categoryId = null,
        int? transferToAccountId = null,
        string? description = null,
        DateTime? transactionDate = null,
        int? recurringTransactionId = null
    ) => new Transaction
        {
            UserId = userId,
            AccountId = accountId,
            CategoryId = categoryId,
            Type = type,
            Amount = amount,
            TransferToAccountId = transferToAccountId,
            Description = description,
            TransactionDate = transactionDate ?? DateTime.UtcNow,
            IsRecurring = recurringTransactionId != null ? true : false,
            RecurringTransactionId = recurringTransactionId
        };

    public static async Task<Transaction> CreateAsync(
        PitakaDbContext context,
        int userId, 
        int accountId, 
        TransactionType type = TransactionType.Income,
        decimal amount = 100,
        int? categoryId = null,
        int? transferToAccountId = null,
        string? description = null,
        DateTime? transactionDate = null,
        int? recurringTransactionId = null
    )
    {
        var transaction = Make(
            userId, accountId, type, amount, categoryId, transferToAccountId, 
            description, transactionDate, recurringTransactionId
        );
        
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        return transaction;
    }
}