namespace PitakaApp.Api.Inputs;

public record UpdateGoalContributionInput (
    DateTime? ContributionDate,
    string? Note
);