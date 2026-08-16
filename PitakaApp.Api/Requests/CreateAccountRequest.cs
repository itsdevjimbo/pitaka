using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record CreateAccountRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required]
    AccountType Type,

    decimal InitialBalance = 0
)
{
    public CreateAccountInput ToInput() =>
        new(Name: Name, Type: Type, InitialBalance: InitialBalance);
}