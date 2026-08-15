namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;

public record BudgetInput (
    int? CategoryId,
    decimal AmountLimit,
    BudgetPeriod Period,
    DateOnly StartDate,
    DateOnly? EndDate
);