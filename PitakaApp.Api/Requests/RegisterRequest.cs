using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record RegisterRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required, EmailAddress, MaxLength(255)]
    string Email,

    // StringLength rather than the codebase's usual MaxLength because this is the one
    // field that needs a floor. Length only — no complexity rules by decision; the 8 is
    // a deliberately easy-to-tighten placeholder, not a researched number.
    [Required, StringLength(128, MinimumLength = 8)]
    string Password
)
{
    public RegisterInput ToInput() =>
        new(Name: Name, Email: Email, Password: Password);
}
