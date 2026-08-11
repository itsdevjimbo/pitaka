namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

// Service-layer input, deliberately separate from CreateCategoryRequest (the web DTO in
// Controllers). No [Required]/[MaxLength] here — those are HTTP concerns. This type can
// be constructed from anywhere (tests, a background job, another controller) without any
// awareness that ASP.NET Core model binding exists.
public record CreateUserOwnedCategoryInput(
    User User,
    string Name,
    CategoryType Type,
    string? Description = null,
    string? Icon = null,
    string? Color = null
);