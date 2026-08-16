namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;

public record BudgetInput (
    int? CategoryId,
    string Name,
    decimal AmountLimit,
    BudgetPeriod Period,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description
);