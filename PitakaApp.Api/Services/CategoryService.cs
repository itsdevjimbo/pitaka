using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class CategoryService(
    PitakaDbContext context,
    CheckUserScopedNameExists checkUserScopedNameExists
)
{
    private readonly PitakaDbContext _context = context;
    private readonly CheckUserScopedNameExists _checkUserScopedNameExists =
        checkUserScopedNameExists;

    public async Task<List<Category>> GetAllForUser(User user) =>
        await _context
            .Categories.AsNoTracking()
            .Where(c => c.IsDefault || c.UserId == user.Id)
            .ToListAsync();

    public async Task<List<Category>> GetSystemDefaults() =>
        await _context.Categories.AsNoTracking().Where(c => c.IsDefault).ToListAsync();

    public async Task<Category?> GetByIdForUser(User user, int id) =>
        await _context
            .Categories.AsNoTracking()
            .Where(c => c.Id == id && (c.UserId == user.Id || c.IsDefault))
            .FirstOrDefaultAsync();

    public async Task<Category?> GetTrackedByIdForUserAsync(
        int userId,
        int id,
        CancellationToken cancellationToken
    ) =>
        await _context
            .Categories.Where(category => category.Id == id && category.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> IsDefaultAsync(int id, CancellationToken cancellationToken) =>
        await _context.Categories.AnyAsync(
            category => category.Id == id && category.IsDefault,
            cancellationToken
        );

    public Task<bool> NameExistsForUserAsync(
        int userId,
        string name,
        int? excludeId = null,
        CancellationToken cancellationToken = default
    ) =>
        _checkUserScopedNameExists.ExecuteAsync(
            _context.Categories,
            userId,
            name,
            category => category.UserId,
            category => category.Name,
            category => category.Id,
            excludeId,
            cancellationToken
        );

    public async Task<Category> CreateUserOwnedAsync(
        User user,
        CreateCategoryInput input,
        CancellationToken cancellationToken = default
    )
    {
        var category = new Category
        {
            UserId = user.Id,
            Name = input.Name,
            Type = input.Type,
            Description = input.Description,
            Icon = input.Icon,
            Color = input.Color,
        };

        _context.Categories.Add(category);

        await _context.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<Category> UpdateAsync(
        Category category,
        UpdateCategoryInput input,
        CancellationToken cancellationToken = default
    )
    {
        category.Name = input.Name;
        category.Description = input.Description;
        category.Icon = input.Icon;
        category.Color = input.Color;

        await _context.SaveChangesAsync(cancellationToken);

        return category;
    }

    public async Task<Category> PatchActiveStatusAsync(
        Category category,
        PatchCategoryActiveInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (input.IsActive)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task DeleteAsync(Category category, CancellationToken cancellationToken = default)
    {
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // No user scoping is intentional. VerifyTransactionCategory and
    // VerifyBudgetCategory already confine every reference to the category's owner,
    // and system defaults are unreachable behind the Forbid at CategoriesController.
    // A WHERE UserId here would be redundant and would read as if cross-user
    // references were possible.
    public async Task<bool> IsInUseAsync(
        int categoryId,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .Transactions.AsNoTracking()
            .AnyAsync(t => t.CategoryId == categoryId, cancellationToken)
        || await _context
            .Budgets.AsNoTracking()
            .AnyAsync(b => b.CategoryId == categoryId, cancellationToken)
        || await _context
            .RecurringTransactions.AsNoTracking()
            .AnyAsync(rt => rt.CategoryId == categoryId, cancellationToken);
}
