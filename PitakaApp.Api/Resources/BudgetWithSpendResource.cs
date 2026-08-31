using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

// The eight BudgetResource fields plus the figures that make a Budget readable as progress:
// how much of the ceiling is gone and the cycle that was summed over. Following the
// GoalWithCurrentAmountResource precedent. The write endpoints keep BudgetResource.
public record BudgetWithSpendResource(
    int Id,
    string Name,
    decimal AmountLimit,
    BudgetPeriod Period,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? CategoryId,
    string? Description,
    decimal AmountSpent,
    DateOnly CycleStart,
    DateOnly CycleEnd
)
{
    public static BudgetWithSpendResource FromModel(
        Budget budget, decimal amountSpent, DateOnly cycleStart, DateOnly cycleEnd) =>
        new(
            budget.Id,
            budget.Name,
            budget.AmountLimit,
            budget.Period,
            budget.StartDate,
            budget.EndDate,
            budget.CategoryId,
            budget.Description,
            amountSpent,
            cycleStart,
            cycleEnd
        );
}
