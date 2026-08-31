using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

// Sums the expenses that count against a Budget inside a resolved cycle window. Paired with
// GetBudgetCycle, which supplies the window. See .scratch/budget-cycle-spend/spec.md.
public class GetBudgetAmountSpent
{
    private readonly PitakaDbContext _context;

    public GetBudgetAmountSpent(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetAsync(Budget budget, DateOnly cycleStart, DateOnly cycleEnd)
    {
        // Half-open on the UTC calendar day: >= CycleStart 00:00 and < CycleEnd + 1 day 00:00,
        // matching the from-inclusive / to-exclusive convention #65 shipped.
        var from = cycleStart.ToDateTime(TimeOnly.MinValue);
        var to = cycleEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

        return await _context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == budget.UserId)
            // Type == Expense excludes Income and Transfer on its own.
            .Where(t => t.Type == TransactionType.Expense)
            // Exact category match; a null CategoryId Budget counts every expense in the window,
            // including uncategorised ones. No rollup to descendant categories.
            .Where(t => budget.CategoryId == null || t.CategoryId == budget.CategoryId)
            .Where(t => t.TransactionDate >= from && t.TransactionDate < to)
            .SumAsync(t => t.Amount);
    }
}
