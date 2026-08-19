namespace PitakaApp.Api.Inputs;

public record CreateGoalContributionInput (
    int? TransactionId,
    decimal Amount,
    DateTime ContributionDate,
    string? Note = null
);