using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// The whole body of PUT api/profile. Name genuinely is the entire writable
// representation of a Profile — email moves through the pending-email flow and password
// is not a field on the read — so this is PUT, not PATCH.
//
// Validation is registration's rules verbatim ([Required, MaxLength(255)]): no minimum,
// no trim, no character rules. A name a Profile was created with must never be rejected
// by that Profile's own screen. Tightening (whitespace-only names, trimming) is a
// separate change applied to RegisterRequest too, and is out of scope here.
public record UpdateProfileRequest([Required, MaxLength(255)] string Name)
{
    public ChangeProfileNameInput ToInput() => new(Name: Name);
}
