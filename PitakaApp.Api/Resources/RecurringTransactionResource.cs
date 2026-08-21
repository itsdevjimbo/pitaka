using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record RecurringTransactionResource(
    int Id,
    int AccountId,
    int? CategoryId,
    string Name,
    RecurringTransactionType Type,
    decimal Amount,
    string? Description,
    Frequency Frequency,
    DateOnly StartDate, 
    DateOnly? EndDate,
    DateOnly NextRunDate,
    RecurringTransactionStatus Status
)
{
    public static RecurringTransactionResource FromModel(RecurringTransaction rt) =>
        new (
            rt.Id, 
            rt.AccountId,
            rt.CategoryId,
            rt.Name,
            rt.Type,
            rt.Amount,
            rt.Description,
            rt.Frequency,
            rt.StartDate,
            rt.EndDate,
            rt.NextRunDate,
            rt.Status
        );

    public static List<RecurringTransactionResource> Collection(IEnumerable<RecurringTransaction> recurringTransactions) =>
        recurringTransactions.Select(FromModel).ToList();
} 