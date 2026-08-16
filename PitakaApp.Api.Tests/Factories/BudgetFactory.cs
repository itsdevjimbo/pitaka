using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class BudgetFactory
{
    public static Budget Make(
        int userId, string? name = null, int? categoryId = null, decimal? amountLimit = null, 
        BudgetPeriod? period = null, DateOnly? startDate = null, DateOnly? endDate = null, string? description = null
    )
    {
        return new Budget
        {
            UserId = userId,
            Name = name ?? "Test budget",
            CategoryId = categoryId,
            AmountLimit = amountLimit ?? 10000,
            Period = period ?? BudgetPeriod.Weekly,
            StartDate = startDate ?? DateOnly.FromDateTime(DateTime.Now),
            EndDate = endDate,
            Description = description
        };
    }

    public static async Task<Budget> CreateAsync(
        PitakaDbContext context, int userId, string? name = null, int? categoryId = null, decimal? amountLimit = null, 
        BudgetPeriod? period = null, DateOnly? startDate = null, DateOnly? endDate = null, string? description = null)
    {
        var budget = Make(userId, name, categoryId, amountLimit, period, startDate, endDate, description);
        context.Budgets.Add(budget);
        await context.SaveChangesAsync();
        return budget;
    }
}