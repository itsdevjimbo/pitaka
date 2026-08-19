using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateGoalContributionRequest (
    DateTime? ContributionDate,
    string? Note
)
{
    public UpdateGoalContributionInput ToInput() =>
        new(ContributionDate, Note);
}