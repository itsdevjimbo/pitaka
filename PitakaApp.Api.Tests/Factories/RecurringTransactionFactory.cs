using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class RecurringTransactionFactory
{
    public static RecurringTransaction Make(
        int userId,
        int accountId,
        int? categoryId = null,
        string? name = null,
        RecurringTransactionType? type = null,
        decimal? amount = null,
        string? description = null,
        Frequency? frequency = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        DateOnly? nextRunDate = null,
        RecurringTransactionStatus? status = null
    )
    {
        return new RecurringTransaction
        {
            UserId = userId,
            AccountId = accountId,
            CategoryId = categoryId,
            Name = name ?? "Test recurring transaction",
            Type = type ?? RecurringTransactionType.Income,
            Amount = amount ?? 500,
            Description = description,
            Frequency = frequency ?? Frequency.Daily,
            StartDate = startDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            EndDate = endDate,
            NextRunDate = nextRunDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = status ?? RecurringTransactionStatus.Active,
        };
    }

    public static async Task<RecurringTransaction> CreateAsync(
        PitakaDbContext context, 
        int userId,
        int accountId,
        int? categoryId = null,
        string? name = null,
        RecurringTransactionType? type = null,
        decimal? amount = null,
        string? description = null,
        Frequency? frequency = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        DateOnly? nextRunDate = null,
        RecurringTransactionStatus? status = null
    )
    {
        var recurringTransaction = Make(userId, accountId, categoryId, name, type, amount, description, frequency, startDate, endDate, nextRunDate, status);
        context.RecurringTransactions.Add(recurringTransaction);
        await context.SaveChangesAsync();
        return recurringTransaction;
    }
}