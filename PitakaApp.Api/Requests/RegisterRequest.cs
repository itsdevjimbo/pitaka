using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record RegisterRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required, EmailAddress, MaxLength(255)]
    string Email,

    // StringLength rather than the codebase's usual MaxLength because this is the one
    // field that needs a floor. The floor and ceiling live in PasswordRules, shared
    // with ResetPasswordRequest.
    [Required, StringLength(PasswordRules.MaxLength, MinimumLength = PasswordRules.MinLength)]
    string Password
)
{
    public RegisterInput ToInput() =>
        new(Name: Name, Email: Email, Password: Password);
}
