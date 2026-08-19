using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;
public record CreateGoalContributionRequest (
    [Required] 
    int GoalId,

    [Required] 
    int AccountId,

    int? TransactionId,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")] 
    decimal Amount,

    [Required] 
    DateTime ContributionDate,

    string? Note
)
{
    public CreateGoalContributionInput ToInput() =>
        new(TransactionId, Amount, ContributionDate, Note);
}