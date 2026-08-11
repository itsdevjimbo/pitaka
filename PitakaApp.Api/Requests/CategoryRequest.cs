using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

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
    string? Color
)
{
    public CreateUserOwnedCategoryInput ToCreateInput(User user) =>
        new(User: user, Name: Name, Type: Type, Description: Description, Icon: Icon, Color: Color);

    public UpdateCategoryInput ToUpdateInput() => 
        new(Name: Name, Type: Type, Description: Description, Icon: Icon, Color: Color);
}