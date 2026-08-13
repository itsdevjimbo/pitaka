namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

public record CreateAccountInput (
    User User,
    string Name,
    AccountType Type,
    decimal InitialBalance = 0
);