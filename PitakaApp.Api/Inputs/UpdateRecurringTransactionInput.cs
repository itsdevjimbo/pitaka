namespace PitakaApp.Api.Inputs;

public record UpdateRecurringTransactionInput (
    string Name,
    decimal Amount,
    int? CategoryId,
    string? Description,
    DateOnly? EndDate
);