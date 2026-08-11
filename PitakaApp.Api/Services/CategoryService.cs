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

    public async Task<Category> CreateUserOwnedAsync(CreateUserOwnedCategoryInput input)
    {
        var category = new Category
        {
            UserId = input.User.Id,
            Name = input.Name,
            Type = input.Type,
            Description = input.Description,
            Icon = input.Icon,
            Color = input.Color,
        };

        _context.Categories.Add(category);

        await _context.SaveChangesAsync();
        return category;
    }

    public async Task<Category> UpdateAsync(Category category, UpdateCategoryInput input)
    {
        category.Name = input.Name;
        category.Type = input.Type;
        category.Description = input.Description;
        category.Icon = input.Icon;
        category.Color = input.Color;

        await _context.SaveChangesAsync();

        return category;
    }
}