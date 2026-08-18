namespace PitakaApp.Api.Inputs;

public record GoalInput (
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate
);