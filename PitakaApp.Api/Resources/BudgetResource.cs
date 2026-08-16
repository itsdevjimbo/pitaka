using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record BudgetResource(
    int Id, 
    string Name, 
    decimal AmountLimit, 
    BudgetPeriod Period, 
    DateOnly StartDate, 
    DateOnly? EndDate, 
    int? CategoryId,
    string? Description
)
{
    public static BudgetResource FromModel(Budget budget) =>
        new (
            budget.Id, 
            budget.Name, 
            budget.AmountLimit, 
            budget.Period, 
            budget.StartDate, 
            budget.EndDate, 
            budget.CategoryId, 
            budget.Description
        );

    public static List<BudgetResource> Collection(IEnumerable<Budget> categories) =>
        categories.Select(FromModel).ToList();
} 