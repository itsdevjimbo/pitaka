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
            t.Id, t.UserId, t.AccountId, t.Type, t.Amount, TransactionDateForWire(t), t.IsRecurring,
            t.CategoryId, t.RecurringTransactionId, t.TransferToAccountId, t.Description, TagResource.Collection(t.Tags)
        );

    // A person-recorded Transaction is stored as a UTC instant, but a list read comes
    // back through MySQL datetime(6) as Kind=Unspecified and System.Text.Json then emits
    // no zone designator — so a UTC instant and a wall-clock day look identical on the
    // wire. Stamp the instant back as UTC so it serialises the 'Z' the write response
    // already does. A generated transaction genuinely is a wall-clock day with no instant
    // behind it and must stay naive; it is the one kind of row that carries a
    // RecurringTransactionId (see CONTEXT.md), which is exactly how pitaka-web tells them
    // apart. The deeper "one column, two frames" question is issue #72.
    private static DateTime TransactionDateForWire(Transaction t) =>
        t.RecurringTransactionId is null
            ? DateTime.SpecifyKind(t.TransactionDate, DateTimeKind.Utc)
            : t.TransactionDate;

    public static List<TransactionResource> Collection(IEnumerable<Transaction> transactions) =>
        transactions.Select(FromModel).ToList();
} 