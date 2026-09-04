using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateGoalContributionRequest (
    DateOnly? ContributionDate = null,
    string? Note = null
)
{
    public UpdateGoalContributionInput ToInput() =>
        new(ContributionDate, Note);
}