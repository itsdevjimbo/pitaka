using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record CreateRecurringTransactionRequest (
    [Required]
    int AccountId,

    int? CategoryId,

    [Required, MaxLength(255)]
    string Name,

    [Required]
    RecurringTransactionType Type,

    [Required]
    Frequency Frequency,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,

    string? Description,

    [Required]
    DateOnly StartDate,

    DateOnly? EndDate
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