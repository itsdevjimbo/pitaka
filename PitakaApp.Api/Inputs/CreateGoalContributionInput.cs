namespace PitakaApp.Api.Inputs;

public record CreateGoalContributionInput (
    int AccountId,
    int? TransactionId,
    decimal Amount,
    DateTime ContributionDate,
    string? Note = null
);