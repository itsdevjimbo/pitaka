using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateAccountRequest (
    [Required, MaxLength(255)]
    string Name
)
{
    public UpdateAccountInput ToInput() => new(Name: Name);
}