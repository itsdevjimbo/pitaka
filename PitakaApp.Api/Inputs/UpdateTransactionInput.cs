namespace PitakaApp.Api.Inputs;

public record UpdateTransactionInput (
    DateTime? TransactionDate = null,
    int? CategoryId = null,
    string? Description = null
);