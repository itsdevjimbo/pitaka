using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Dtos;

public record GoalWithCurrentAmount (
    int Id, string Name, decimal TargetAmount, DateOnly? TargetDate, 
    GoalStatus Status, decimal CurrentAmount
);