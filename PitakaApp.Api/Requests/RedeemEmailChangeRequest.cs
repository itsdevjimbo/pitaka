using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// The Profile id and the token, the same shape confirm-email already uses. The new
// address is not carried here — redemption checks the token against the address stored
// as the pending email, not one the caller supplies (ADR 0014, spec decision 18).
public record RedeemEmailChangeRequest (
    [Required]
    int UserId,

    [Required]
    string Token
)
{
    public RedeemEmailChangeInput ToInput() =>
        new(UserId: UserId, Token: Token);
}
