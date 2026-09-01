using PitakaApp.Api.Models;
using PitakaApp.Api.Resources;

namespace PitakaApp.Api.Actions;

// Enriches a Budget into a BudgetWithSpendResource: resolves its cycle window, sums the expenses
// that count against it inside that window, and assembles the resource. The one place the cycle
// rule and the sum rule are wired together, so GET /api/budgets/{id} and GET /api/budgets report
// an identical AmountSpent for the same Budget. See .scratch/budget-cycle-spend/spec.md.
public class GetBudgetWithSpend
{
    private readonly GetBudgetCycle _getBudgetCycle;
    private readonly GetBudgetAmountSpent _getBudgetAmountSpent;

    public GetBudgetWithSpend(GetBudgetCycle getBudgetCycle, GetBudgetAmountSpent getBudgetAmountSpent)
    {
        _getBudgetCycle = getBudgetCycle;
        _getBudgetAmountSpent = getBudgetAmountSpent;
    }

    public async Task<BudgetWithSpendResource> ForBudgetAsync(Budget budget)
    {
        var (cycleStart, cycleEnd) = _getBudgetCycle.ForBudget(budget);
        var amountSpent = await _getBudgetAmountSpent.GetAsync(budget, cycleStart, cycleEnd);

        return BudgetWithSpendResource.FromModel(budget, amountSpent, cycleStart, cycleEnd);
    }
}
