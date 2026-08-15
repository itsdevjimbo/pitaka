using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record BudgetResource(int Id, decimal AmountLimit, BudgetPeriod Period, DateOnly StartDate, DateOnly? EndDate, int? CategoryId)
{
    public static BudgetResource FromModel(Budget budget) =>
        new(budget.Id, budget.AmountLimit, budget.Period, budget.StartDate, budget.EndDate, budget.CategoryId);

    public static List<BudgetResource> Collection(IEnumerable<Budget> categories) =>
        categories.Select(FromModel).ToList();
} 