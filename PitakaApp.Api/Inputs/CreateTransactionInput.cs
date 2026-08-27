using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Inputs;

public record CreateTransactionInput (
    TransactionType Type,
    decimal Amount,
    DateTime? TransactionDate = null,
    int? CategoryId = null,
    int? TransferToAccountId = null,
    string? Description = null,
    bool? IsRecurring = null,
    int? RecurringTransactionId = null
);