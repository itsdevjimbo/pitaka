using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record TransactionResource(
    int Id,
    int UserId,
    int AccountId,
    TransactionType Type,
    decimal Amount,
    DateTime TransactionDate,
    bool IsRecurring,
    int? CategoryId,
    int? RecurringTransactionId,
    int? TransferToAccountId,
    string? Description,
    List<TagResource> Tags
)
{
    public static TransactionResource FromModel(Transaction t) =>
        new(
            t.Id, t.UserId, t.AccountId, t.Type, t.Amount, t.TransactionDate, t.IsRecurring,
            t.CategoryId, t.RecurringTransactionId, t.TransferToAccountId, t.Description, TagResource.Collection(t.Tags)
        );

    public static List<TransactionResource> Collection(IEnumerable<Transaction> transactions) =>
        transactions.Select(FromModel).ToList();
} 