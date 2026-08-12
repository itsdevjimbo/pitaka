namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

public record CreateUserOwnedCategoryInput (
    User User,
    string Name,
    CategoryType Type,
    string? Description = null,
    string? Icon = null,
    string? Color = null,
    int? ParentId = null
);