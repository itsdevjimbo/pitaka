using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// Contribution facts are immutable. Correct a date by deleting and recreating the row;
// PUT only replaces its optional note.
public record UpdateGoalContributionRequest(string? Note = null)
{
    public UpdateGoalContributionInput ToInput() => new(Note);
}
