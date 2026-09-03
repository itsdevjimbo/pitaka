namespace PitakaApp.Api.Inputs;

public record UpdateCategoryInput (
    string Name,
    string? Description = null,
    string? Icon = null,
    string? Color = null
);
