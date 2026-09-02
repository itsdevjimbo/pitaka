using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public enum BudgetCategoryVerdict
{
    Ok,
    NotFound,
    NotExpense,
}

// A Budget's Category, when present, must be an expense category visible to the user (their
// own or a system default): only expenses count against a Budget, so an income-narrowed
// Budget reports zero spent forever. Sibling in shape to VerifyCategoryExistence, which
// TransactionsController and RecurringTransactionsController share and which does not want
// the type rule — this is the Budget-only variant. One query answers both "is it visible"
// and "is it an expense category", and the verdict keeps the two failures apart so the
// caller can word them differently. See .scratch/budget-expense-category/spec.md.
public class VerifyBudgetCategory
{
    private readonly PitakaDbContext _context;

    public VerifyBudgetCategory(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<BudgetCategoryVerdict> VerifyAsync(User user, int categoryId)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId && (c.UserId == user.Id || c.IsDefault));

        if (category == null)
        {
            return BudgetCategoryVerdict.NotFound;
        }

        return category.Type == CategoryType.Expense
            ? BudgetCategoryVerdict.Ok
            : BudgetCategoryVerdict.NotExpense;
    }
}
