namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;

public record CategoryInput (
    string Name,
    CategoryType Type,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    int? ParentId = null
);