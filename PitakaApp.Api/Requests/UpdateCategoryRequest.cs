using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateCategoryRequest (
    [Required, MaxLength(255)]
    string Name,

    string? Description,

    [MaxLength(100)]
    string? Icon,

    [MaxLength(100)]
    string? Color,

    int? ParentId
)
{
    public UpdateCategoryInput ToInput() =>
        new(Name: Name, Description: Description, Icon: Icon, Color: Color, ParentId: ParentId);
}
