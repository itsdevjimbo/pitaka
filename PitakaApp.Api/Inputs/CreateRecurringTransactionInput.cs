namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;

public record CreateRecurringTransactionInput (
    int AccountId,
    int? CategoryId,
    string Name,
    RecurringTransactionType Type,
    decimal Amount,
    string? Description,
    Frequency Frequency,
    DateOnly StartDate,
    DateOnly? EndDate
);