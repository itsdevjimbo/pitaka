using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// Parameters are ordered required-before-optional so the optional ones can carry a
// default: with RespectRequiredConstructorParameters on, a parameter without a default
// is mandatory in the body. GoalId, AccountId, Amount and ContributionDate are the
// four a contribution cannot be recorded without. See ADR 0009.
public record CreateGoalContributionRequest (
    [Required]
    int GoalId,

    [Required]
    int AccountId,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,

    [Required]
    DateOnly ContributionDate,

    int? TransactionId = null,

    string? Note = null
)
{
    public CreateGoalContributionInput ToInput() =>
        new(TransactionId, Amount, ContributionDate, Note);
}
