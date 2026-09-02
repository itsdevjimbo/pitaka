using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class CategoryService
{
    private readonly PitakaDbContext _context;

    public CategoryService(PitakaDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Category>> GetAllForUser(User user) =>
        await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsDefault || c.UserId == user.Id)
            .ToListAsync();

    public async Task<List<Category>> GetSystemDefaults() =>
        await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsDefault)
            .ToListAsync();

    public async Task<Category?> GetByIdForUser(User user, int id) =>
        await _context.Categories
            .AsNoTracking()
            .Where(c => c.Id == id && (c.UserId == user.Id || c.IsDefault))
            .FirstOrDefaultAsync();

    public async Task<Category?> GetTrackedByIdAsync(int id) =>
        await _context.Categories
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();

    // excludeId lets Update check "does any OTHER category of mine already have this
    // name" without the category being renamed conflicting with itself.
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.UserId == userId && c.Name == name && (excludeId == null || c.Id != excludeId));

    // A parent must be visible to this user (system default or their own — same rule
    // as GetByIdForUser) and, when updating, can't be the category itself. Note: this
    // only catches direct self-reference (A -> A), not deeper cycles (A -> B -> A) —
    // walking the full ancestor chain is a known gap, not handled here.
    public async Task<bool> IsValidParentAsync(User user, int parentId, int? excludeId = null)
    {
        if (excludeId != null && parentId == excludeId)
        {
            return false;
        }

        return await _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id == parentId && (c.IsDefault || c.UserId == user.Id));
    }

    public async Task<Category> CreateUserOwnedAsync(User user, CreateCategoryInput input)
    {
        var category = new Category
        {
            UserId = user.Id,
            Name = input.Name,
            Type = input.Type,
            Description = input.Description,
            Icon = input.Icon,
            Color = input.Color,
            ParentId = input.ParentId,
        };

        _context.Categories.Add(category);

        await _context.SaveChangesAsync();
        return category;
    }

    public async Task<Category> UpdateAsync(Category category, UpdateCategoryInput input)
    {
        category.Name = input.Name;
        category.Description = input.Description;
        category.Icon = input.Icon;
        category.Color = input.Color;
        category.ParentId = input.ParentId;

        await _context.SaveChangesAsync();

        return category;
    }

    public async Task DeleteAsync(Category category)
    {
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
    }
}