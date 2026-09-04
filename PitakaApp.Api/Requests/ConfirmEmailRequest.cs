using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record ConfirmEmailRequest (
    [Required]
    int UserId,

    [Required]
    string Token
)
{
    public ConfirmEmailInput ToInput() =>
        new(UserId: UserId, Token: Token);
}
