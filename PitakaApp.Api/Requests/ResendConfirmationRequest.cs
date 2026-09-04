using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record ResendConfirmationRequest (
    // [EmailAddress] says "that is not an address", never "no Profile has it" — same
    // reasoning as ForgotPasswordRequest.Email.
    [Required, EmailAddress, MaxLength(255)]
    string Email
)
{
    public ResendConfirmationInput ToInput() =>
        new(Email: Email);
}
