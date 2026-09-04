using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public enum TransactionCategoryVerdict
{
    Ok,
    NotFound,
    TypeMismatch,
}

// A Transaction's Category, when present, must be visible to the user (their own or a system
// default) and carry the same type as the Transaction: an Income transaction files under an
// Income category, an Expense transaction under an Expense category. A Transfer never reaches
// here — it cannot carry a category at all (#63) — so the caller passes only Income or
// Expense as the expected type. Sibling in shape to VerifyBudgetCategory; both exist because
// VerifyCategoryExistence answered only "is it visible" and the type rule needs the Category
// row itself. One query answers both questions, and the verdict keeps the two failures apart
// so the caller can word them differently. This is the Transaction-side reader ADR 0003
// names when it argues a Category's type must be permanent; see GitHub issue #76.
public class VerifyTransactionCategory
{
    private readonly PitakaDbContext _context;

    public VerifyTransactionCategory(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<TransactionCategoryVerdict> VerifyAsync(User user, int categoryId, CategoryType expectedType)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId && (c.UserId == user.Id || c.IsDefault));

        if (category == null)
        {
            return TransactionCategoryVerdict.NotFound;
        }

        return category.Type == expectedType
            ? TransactionCategoryVerdict.Ok
            : TransactionCategoryVerdict.TypeMismatch;
    }
}
