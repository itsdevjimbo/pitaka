namespace PitakaApp.Api.Inputs;

public record CreateGoalContributionInput (
    int? TransactionId,
    decimal Amount,
    DateOnly ContributionDate,
    string? Note = null
);