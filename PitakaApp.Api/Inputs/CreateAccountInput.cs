namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;

public record CreateAccountInput (
    string Name,
    AccountType Type,
    decimal InitialBalance = 0
);