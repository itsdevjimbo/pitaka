using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record CreateCategoryRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required]
    CategoryType Type,

    string? Description = null,

    [MaxLength(100)]
    string? Icon = null,

    [MaxLength(100)]
    string? Color = null
)
{
    public CreateCategoryInput ToInput() =>
        new(Name: Name, Type: Type, Description: Description, Icon: Icon, Color: Color);
}
