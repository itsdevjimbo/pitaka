using PitakaApp.Api.Dtos;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record GoalWithCurrentAmountResource(int Id, string Name, decimal TargetAmount, DateOnly? TargetDate, GoalStatus Status, decimal CurrentAmount)
{
    public static GoalWithCurrentAmountResource FromModel(Goal goal, decimal currentAmount) =>
        new(goal.Id, goal.Name, goal.TargetAmount, goal.TargetDate, goal.Status, currentAmount);

    public static GoalWithCurrentAmountResource FromDto(GoalWithCurrentAmount dto) =>
        new(dto.Id, dto.Name, dto.TargetAmount, dto.TargetDate, dto.Status, dto.CurrentAmount);

    public static List<GoalWithCurrentAmountResource> FromDtoCollection(IEnumerable<GoalWithCurrentAmount> dtos) =>
        dtos.Select(FromDto).ToList();
} 