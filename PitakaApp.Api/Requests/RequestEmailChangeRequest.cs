using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record RequestEmailChangeRequest (
    // [EmailAddress] says "that is not an address", never "that address is taken" — the
    // taken-address answer is the action's 409, same split as ForgotPasswordRequest.
    [Required, EmailAddress, MaxLength(255)]
    string NewEmail,

    // [Required] only, no length rule: this is an existing password being re-checked,
    // not a new one being set. A length floor here would leak the password policy and
    // make a too-short wrong password 400 while a long one 401.
    [Required]
    string CurrentPassword
)
{
    public RequestEmailChangeInput ToInput() =>
        new(NewEmail: NewEmail, CurrentPassword: CurrentPassword);
}
