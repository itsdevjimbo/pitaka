using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record TransactionQueryRequest(
    int? AccountId = null,
    int? CategoryId = null,
    TransactionType? Type = null,

    // A case-insensitive substring match on a Transaction's `Description` and nothing else —
    // never a Category or Account name. Empty or whitespace-only is absent, not a filter
    // that matches everything. See issue #73.
    string? Description = null,

    // `from`/`to` are half-open calendar bounds that each carry their own zone. A
    // DateTimeOffset holds both an instant and the wall-clock reading it was taken from —
    // the two frames TransactionDate stores (CONTEXT.md, "Time"). A bare timestamp with no
    // designator is a 400, enforced in ZoneBearingDateTimeOffsetModelBinder while the raw
    // text still exists; two bounds with different offsets are a 400, enforced in Validate()
    // below. See ADR 0005.
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,

    [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater.")]
    int? Page = null,

    [Range(1, 200, ErrorMessage = "pageSize must be between 1 and 200.")]
    int? PageSize = null
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is not DateTimeOffset from || To is not DateTimeOffset to)
        {
            yield break;
        }

        // One range, one zone. Reject a mismatch here and stop: with the offsets equal the
        // instants and the wall-clock readings sort the same way, so the inverted-range
        // guard below stays correct as a single instant comparison with no second one
        // beside it — and a caller who has not even settled on one zone does not also need
        // to hear that their range is backwards. See ADR 0005.
        if (from.Offset != to.Offset)
        {
            yield return new ValidationResult(
                "from and to must name the same zone.",
                [nameof(From)]
            );
            yield break;
        }

        if (from >= to)
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
        Description: string.IsNullOrWhiteSpace(Description) ? null : Description,
        From: From,
        To: To,
        Page: Page ?? 1,
        PageSize: PageSize ?? 50
    );
}
