using PitakaApp.Api.Attributes;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateTransactionRequest (
    [RequiresUtcOffset]
    DateTime? TransactionDate = null,
    int? CategoryId = null,
    string? Description = null,
    int[]? TagIds = null
)
{
    public UpdateTransactionInput ToInput() => new (
        TransactionDate: TransactionDate,
        CategoryId: CategoryId,
        Description: Description
    );
}