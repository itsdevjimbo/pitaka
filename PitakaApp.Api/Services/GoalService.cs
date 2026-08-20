using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Dtos;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class GoalService
{
    private readonly PitakaDbContext _context;

    public GoalService(PitakaDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<GoalWithCurrentAmount>> GetAllForUser(User user) =>
        await _context.Goals
            .AsNoTracking()
            .Where(g => g.UserId == user.Id)
            .Select(goal => new GoalWithCurrentAmount (
                goal.Id, goal.Name, goal.TargetAmount, goal.TargetDate, goal.Status, goal.Contributions.Sum(gc => gc.Amount)
            ))
            .ToListAsync();

    public async Task<Goal?> GetByIdForUser(User user, int id) =>
        await _context.Goals
            .AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Goal?> GetTrackedByIdAsync(int id) =>
        await _context.Goals
            .Where(a => a.Id == id)
            .FirstOrDefaultAsync();
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.Goals
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId));

    public async Task<Goal> CreateAsync(User user, GoalInput input)
    {
        var goal = new Goal
        {
            UserId = user.Id,
            Name = input.Name,
            TargetAmount = input.TargetAmount,
            TargetDate = input.TargetDate
        };

        _context.Goals.Add(goal);
        await _context.SaveChangesAsync();
        return goal; 
    }

    public async Task<Goal> UpdateAsync(Goal goal, GoalInput input)
    {
        goal.Name = input.Name;
        goal.TargetAmount = input.TargetAmount;
        goal.TargetDate = input.TargetDate;
        await _context.SaveChangesAsync();
        return goal;
    }

    public async Task<Goal> PatchStatusAsync(Goal goal, GoalStatus status)
    {
        goal.Status = status;
        await _context.SaveChangesAsync();
        return goal;
    }

    public async Task DeleteAsync(Goal goal)
    {
        _context.Goals.Remove(goal);
        await _context.SaveChangesAsync();
    }
}