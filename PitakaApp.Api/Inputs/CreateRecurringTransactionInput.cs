using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

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