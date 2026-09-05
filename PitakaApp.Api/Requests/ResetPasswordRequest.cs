using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record ResetPasswordRequest (
    // Identity's ResetPasswordAsync needs a User before it can check the token — same
    // shape as ConfirmEmailRequest.UserId. Paired pitaka-web issue: the reset screen
    // reads userId from the link's query string alongside the token.
    [Required]
    int UserId,

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
        new(UserId: UserId, Token: Token, Password: Password);
}
