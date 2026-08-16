using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record CategoryRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required]
    CategoryType Type,

    string? Description,

    [MaxLength(100)]
    string? Icon,

    [MaxLength(100)]
    string? Color,

    int? ParentId
)
{
    public CategoryInput ToInput() =>
        new(Name: Name, Type: Type, Description: Description, Icon: Icon, Color: Color, ParentId: ParentId);
}