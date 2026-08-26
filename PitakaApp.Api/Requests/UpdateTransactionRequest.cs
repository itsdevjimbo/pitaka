using PitakaApp.Api.Attributes;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateTransactionRequest (
    [RequiresUtcOffset]
    DateTime? TransactionDate,
    int? CategoryId,
    string? Description,
    int[]? TagIds
)
{
    public UpdateTransactionInput ToInput() => new (
        TransactionDate: TransactionDate,
        CategoryId: CategoryId,
        Description: Description
    );
}