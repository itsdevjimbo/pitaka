using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record ForgotPasswordRequest (
    // [EmailAddress] says "that is not an address", never "no Profile has it", so a
    // malformed value still 400s on Email without touching the enumeration property.
    [Required, EmailAddress, MaxLength(255)]
    string Email
)
{
    public RequestPasswordResetInput ToInput() =>
        new(Email: Email);
}
