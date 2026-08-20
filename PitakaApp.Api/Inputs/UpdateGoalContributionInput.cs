namespace PitakaApp.Api.Inputs;

public record UpdateGoalContributionInput (
    DateOnly? ContributionDate,
    string? Note
);