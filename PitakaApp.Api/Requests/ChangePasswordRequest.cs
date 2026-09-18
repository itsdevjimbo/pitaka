using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// The whole body of POST api/profile/password: the current password, proved not
// assumed, and the replacement.
public record ChangePasswordRequest(
    // [Required] only, no length rule: this is an existing password being re-checked,
    // not a new one being set. A length floor here would leak the password policy and
    // make a too-short wrong password 400 while a long one 401 — same split as
    // RequestEmailChangeRequest.CurrentPassword.
    [Required] string OldPassword,
    // Registration's password rule, on its third consumer (after RegisterRequest and
    // ResetPasswordRequest). A value under the floor 400s here at [ApiController]
    // validation, naming NewPassword, before the action runs — so a weak new password
    // never reaches the change and the current password is still good afterwards.
    [
        Required,
        StringLength(PasswordRules.MaxLength, MinimumLength = PasswordRules.MinLength)
    ] string NewPassword
)
{
    public ChangePasswordInput ToInput() => new(OldPassword: OldPassword, NewPassword: NewPassword);
}
