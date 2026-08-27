using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;


public record GoalContributionResource(
    int Id, 
    int GoalId, 
    int AccountId, 
    int? TransactionId, 
    decimal Amount, 
    DateOnly ContributionDate, 
    string? Note
)
{
    public static GoalContributionResource FromModel(GoalContribution gc) =>
        new(gc.Id, gc.GoalId, gc.AccountId, gc.TransactionId, gc.Amount, gc.ContributionDate, gc.Note);

    public static List<GoalContributionResource> Collection(IEnumerable<GoalContribution> goalContributions) =>
        goalContributions.Select(FromModel).ToList();
} 