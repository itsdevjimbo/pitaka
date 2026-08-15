using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class BudgetFactory
{
    public static Budget Make(
        int userId, int? categoryId = null, decimal? amountLimit = null, 
        BudgetPeriod? period = null, DateOnly? startDate = null, DateOnly? endDate = null
    )
    {
        return new Budget
        {
            UserId = userId,
            CategoryId = categoryId,
            AmountLimit = amountLimit ?? 10000,
            Period = period ?? BudgetPeriod.Weekly,
            StartDate = startDate ?? DateOnly.FromDateTime(DateTime.Now),
            EndDate = endDate
        };
    }

    public static async Task<Budget> CreateAsync(
        PitakaDbContext context, int userId, int? categoryId = null, decimal? amountLimit = null, 
        BudgetPeriod? period = null, DateOnly? startDate = null, DateOnly? endDate = null)
    {
        var budget = Make(userId, categoryId, amountLimit, period, startDate, endDate);
        context.Budgets.Add(budget);
        await context.SaveChangesAsync();
        return budget;
    }
}