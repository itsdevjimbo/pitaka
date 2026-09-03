using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record CreateCategoryRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required]
    CategoryType Type,

    string? Description,

    [MaxLength(100)]
    string? Icon,

    [MaxLength(100)]
    string? Color
)
{
    public CreateCategoryInput ToInput() =>
        new(Name: Name, Type: Type, Description: Description, Icon: Icon, Color: Color);
}
