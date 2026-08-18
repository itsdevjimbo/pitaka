using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class GoalFactory
{
    public static Goal Make(int userId, string? name = null, decimal? targetAmount = null, DateOnly? targetDate = null, GoalStatus? status = null)
    {
        return new Goal
        {
            UserId = userId,
            Name = name ?? "Test goal",
            TargetAmount = targetAmount ?? 10000,
            TargetDate = targetDate,
            Status = status ?? GoalStatus.Active
        };
    }

    public static async Task<Goal> CreateAsync(PitakaDbContext context, int userId, string? name = null, decimal? targetAmount = null, DateOnly? targetDate = null, GoalStatus? status = null)
    {
        var goal = Make(userId, name, targetAmount, targetDate, status);
        context.Goals.Add(goal);
        await context.SaveChangesAsync();
        return goal;
    }
}