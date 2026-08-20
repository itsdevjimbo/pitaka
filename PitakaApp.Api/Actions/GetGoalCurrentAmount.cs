using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class GetGoalCurrentAmount
{
    private readonly PitakaDbContext _context;
    public GetGoalCurrentAmount(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetAsync(Goal goal)
    {
        return await _context.GoalContributions
            .Where(gc => gc.GoalId == goal.Id)
            .SumAsync(gc => gc.Amount);
    }
}