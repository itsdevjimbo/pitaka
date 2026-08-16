using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class BudgetService
{

    private readonly CategoryService _categoryService;
    private readonly PitakaDbContext _context;

    public BudgetService(PitakaDbContext context, CategoryService categoryService)
    {
        _context = context;
        _categoryService = categoryService;
    }
    
    public async Task<List<Budget>> GetAllForUser(User user) =>
        await _context.Budgets
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .ToListAsync();

    public async Task<Budget?> GetByIdForUser(User user, int id) =>
        await _context.Budgets
            .AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Budget?> GetTrackedByIdAsync(int id) =>
        await _context.Budgets
            .Where(a => a.Id == id)
            .FirstOrDefaultAsync();
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.Budgets
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId));

    public async Task<Budget> CreateAsync(User user, BudgetInput input)
    {
        var budget = new Budget
        {
            UserId = user.Id,
            Name = input.Name,
            CategoryId = input.CategoryId,
            AmountLimit = input.AmountLimit,
            Period = input.Period,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Description = input.Description
        };

        _context.Budgets.Add(budget);

        await _context.SaveChangesAsync();
        return budget; 
    }

    public async Task<Budget> UpdateAsync(Budget budget, BudgetInput input)
    {
        budget.Name = input.Name;
        budget.CategoryId = input.CategoryId;
        budget.AmountLimit = input.AmountLimit;
        budget.Period = input.Period;
        budget.StartDate = input.StartDate;
        budget.EndDate = input.EndDate;
        budget.Description = input.Description;

        await _context.SaveChangesAsync();

        return budget;
    }

    public async Task DeleteAsync(Budget budget)
    {
        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> VerifyCategoryExistence(User user, int? categoryId)
    {
        if (categoryId is not int id)
        {
            return true;
        }
        
        var category = await _categoryService.GetByIdForUser(user, id);
        return category != null;
    }
}