using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class BudgetService(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<List<Budget>> GetAllForUser(User user) =>
        await _context.Budgets.AsNoTracking().Where(a => a.UserId == user.Id).ToListAsync();

    public async Task<Budget?> GetByIdForUser(User user, int id) =>
        await _context
            .Budgets.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Budget?> GetTrackedByIdForUserAsync(
        int userId,
        int id,
        CancellationToken cancellationToken
    ) =>
        await _context
            .Budgets.Where(budget => budget.Id == id && budget.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> NameExistsForUserAsync(
        int userId,
        string name,
        int? excludeId = null,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .Budgets.AsNoTracking()
            .AnyAsync(
                a =>
                    a.UserId == userId
                    && a.Name == name
                    && (excludeId == null || a.Id != excludeId),
                cancellationToken
            );

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
            Description = input.Description,
        };

        _context.Budgets.Add(budget);

        await _context.SaveChangesAsync();
        return budget;
    }

    public async Task<Budget> UpdateAsync(
        Budget budget,
        BudgetInput input,
        CancellationToken cancellationToken = default
    )
    {
        budget.Name = input.Name;
        budget.CategoryId = input.CategoryId;
        budget.AmountLimit = input.AmountLimit;
        budget.Period = input.Period;
        budget.StartDate = input.StartDate;
        budget.EndDate = input.EndDate;
        budget.Description = input.Description;

        await _context.SaveChangesAsync(cancellationToken);

        return budget;
    }

    public async Task DeleteAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
