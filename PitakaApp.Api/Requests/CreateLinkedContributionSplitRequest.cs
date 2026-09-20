using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Requests;

public sealed record CreateLinkedContributionSplitRequest(
    [Required] DateOnly ContributionDate,
    [Required] IReadOnlyList<CreateLinkedContributionSplitRowRequest> Contributions
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        for (var index = 0; index < Contributions.Count; index++)
        {
            if (Contributions[index] is null)
            {
                yield return new ValidationResult(
                    "A Contribution row is required.",
                    [$"contributions[{index}]"]
                );
            }
        }
    }

    public LinkedContributionSplitInput ToInput() =>
        new(ContributionDate, [.. Contributions.Select(row => row.ToInput())]);
}

public sealed record CreateLinkedContributionSplitRowRequest(
    [Required] int GoalId,
    [Required] decimal Amount,
    string? Note = null,
    bool AcknowledgeTargetOverrun = false
)
{
    public LinkedContributionSplitRow ToInput() =>
        new(GoalId, Amount, Note, AcknowledgeTargetOverrun);
}
