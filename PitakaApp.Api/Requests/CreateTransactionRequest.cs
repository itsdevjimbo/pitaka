using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Attributes;

namespace PitakaApp.Api.Requests;

public record CreateTransactionRequest (
    [Required]
    int AccountId,

    [Required]
    TransactionType Type,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,
    
    int? CategoryId = null,

    [RequiresUtcOffset]
    DateTime? TransactionDate = null,

    int? TransferToAccountId = null,

    string? Description = null,

    int[]? TagIds = null
): IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == TransactionType.Transfer && TransferToAccountId == AccountId)
        {
            yield return new ValidationResult(
                "A transfer's destination must be a different account from its source.",
                [nameof(TransferToAccountId)]
            );
        }
        
        if (Type == TransactionType.Transfer && TransferToAccountId == null)
        {
            yield return new ValidationResult(
                "TransferToAccountId is required when Type is Transfer.",
                [nameof(TransferToAccountId)]
            );
        }

        if (Type != TransactionType.Transfer && TransferToAccountId != null)
        {
            yield return new ValidationResult(
                "TransferToAccountId must not be set unless Type is Transfer.",
                [nameof(TransferToAccountId)]
            );
        }
        if (Type == TransactionType.Transfer && CategoryId != null)
        {
            yield return new ValidationResult(
                "A transfer cannot be assigned a category.",
                [nameof(CategoryId)]
            );
        }
    }

    public CreateTransactionInput ToInput() =>
        new (
            Type: Type, 
            Amount: Amount,
            TransactionDate: TransactionDate,
            CategoryId: CategoryId,
            TransferToAccountId: TransferToAccountId,
            Description: Description
        );
}