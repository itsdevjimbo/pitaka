namespace PitakaApp.Api.Inputs;

using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

public record CreateTransactionInput (
    Account Account,
    TransactionType Type,
    decimal Amount,
    DateTime? TransactionDate = null,
    int? CategoryId = null,
    int? TransferToAccountId = null,
    string? Description = null,
    bool? IsRecurring = null,
    int? RecurringTransactionId = null
);