using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Requests;

public record RecurringTransactionPatchRequest (
    [Required]
    RecurringTransactionStatus Status
): IValidatableObject
{
    
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == RecurringTransactionStatus.Completed)
        {
            yield return new ValidationResult(
                "Cannot manually set recurring transaction to completed",
                [nameof(Status)]
            );
        }
    }
}