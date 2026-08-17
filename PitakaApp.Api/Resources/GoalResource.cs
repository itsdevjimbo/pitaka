using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record GoalResource(string Name, decimal TargetAmount, DateOnly? TargetDate, GoalStatus Status)
{
    public static GoalResource FromModel(Goal goal) =>
        new(goal.Name, goal.TargetAmount, goal.TargetDate, goal.Status);

    public static List<GoalResource> Collection(IEnumerable<Goal> goals) =>
        goals.Select(FromModel).ToList();
} 