using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record BudgetInput (
    int? CategoryId,
    string Name,
    decimal AmountLimit,
    BudgetPeriod Period,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description
);