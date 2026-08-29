using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record LoginRequest (
    // Presence only. Login does not re-assert the registration password rule, so a
    // Profile created before the rule tightens can still sign in.
    [Required]
    string Email,

    [Required]
    string Password
)
{
    public LoginInput ToInput() =>
        new(Email: Email, Password: Password);
}
