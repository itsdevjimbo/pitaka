using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

// Parameters are ordered required-before-optional so the optional ones can carry a
// default: with RespectRequiredConstructorParameters on, a parameter without a default
// is mandatory in the body. CategoryId, Description and EndDate are the three a
// standing instruction can legitimately omit. See ADR 0008.
public record CreateRecurringTransactionRequest (
    [Required]
    int AccountId,

    [Required, MaxLength(255)]
    string Name,

    [Required]
    RecurringTransactionType Type,

    [Required]
    Frequency Frequency,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,

    [Required]
    DateOnly StartDate,

    int? CategoryId = null,

    string? Description = null,

    DateOnly? EndDate = null
): IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            yield return new ValidationResult(
                "StartDate must be in the future",
                [nameof(StartDate)]
            );
        }

        if (EndDate.HasValue && EndDate.Value <= StartDate)
        {
            yield return new ValidationResult(
                "EndDate must be after StartDate.",
                [nameof(EndDate)]
            );
        }
    }

    public CreateRecurringTransactionInput ToInput() =>
        new (AccountId, CategoryId, Name, Type, Amount, Description, Frequency, StartDate, EndDate);
}
