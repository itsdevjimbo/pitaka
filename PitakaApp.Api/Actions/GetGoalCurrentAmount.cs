using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class GetGoalCurrentAmount(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<decimal> GetAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        return await _context
            .GoalContributions.Where(gc => gc.GoalId == goal.Id)
            .SumAsync(gc => gc.Amount, cancellationToken);
    }
}
