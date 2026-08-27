using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record CategoryInput (
    string Name,
    CategoryType Type,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    int? ParentId = null
);