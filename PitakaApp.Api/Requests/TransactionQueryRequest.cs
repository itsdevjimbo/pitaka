using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record TransactionQueryRequest(
    int? AccountId = null,
    int? CategoryId = null,
    TransactionType? Type = null,
    DateTime? From = null,
    DateTime? To = null,

    [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater.")]
    int? Page = null,

    [Range(1, 200, ErrorMessage = "pageSize must be between 1 and 200.")]
    int? PageSize = null
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is DateTime from && To is DateTime to && from >= to)
        {
            yield return new ValidationResult(
                "from must be strictly earlier than to.",
                [nameof(From)]
            );
        }
    }

    public TransactionQueryInput ToInput() => new(
        AccountId: AccountId,
        CategoryId: CategoryId,
        Type: Type,
        From: From,
        To: To,
        Page: Page ?? 1,
        PageSize: PageSize ?? 50
    );
}
