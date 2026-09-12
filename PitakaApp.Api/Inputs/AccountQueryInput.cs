using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record AccountQueryInput(
    AccountType? Type,
    bool? IsActive
);
