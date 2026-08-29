using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record ResetPasswordRequest (
    // The token identifies the Profile through the store. No email address — one less
    // thing to thread through a screen the person reached from a mail client, and one
    // less check whose failure is an oracle.
    [Required]
    string Token,

    // The registration password rule, on its second consumer. A value under the floor
    // 400s here at [ApiController] validation, before the action runs, so the token is
    // never spent by a rejected attempt.
    [Required, StringLength(PasswordRules.MaxLength, MinimumLength = PasswordRules.MinLength)]
    string Password
)
{
    public ResetPasswordInput ToInput() =>
        new(Token: Token, Password: Password);
}
