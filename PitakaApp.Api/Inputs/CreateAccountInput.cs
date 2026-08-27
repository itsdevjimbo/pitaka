using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record CreateAccountInput (
    string Name,
    AccountType Type,
    decimal InitialBalance = 0
);