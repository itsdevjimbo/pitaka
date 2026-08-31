using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record TransactionPageResource(
    List<TransactionResource> Data,
    int Page,
    int PageSize,
    int TotalCount
)
{
    public static TransactionPageResource From(
        IEnumerable<Transaction> transactions, int page, int pageSize, int totalCount
    ) =>
        new(TransactionResource.Collection(transactions), page, pageSize, totalCount);
}
