using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record BudgetRequest (
    [Required, MaxLength(255)]
    string Name, 

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal AmountLimit,
    
    [Required]
    BudgetPeriod Period,

    [Required]
    DateOnly StartDate,

    DateOnly? EndDate = null,

    int? CategoryId = null,

    string? Description = null
): IValidatableObject
{

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate.HasValue && EndDate.Value < StartDate)
        {
            yield return new ValidationResult(
                "EndDate must be on or after StartDate.",
                [nameof(EndDate)]
            );
        }
    }

    public BudgetInput ToInput() =>
        new(CategoryId: CategoryId, Name: Name, AmountLimit: AmountLimit, Period: Period, StartDate: StartDate, EndDate: EndDate, Description: Description);
}