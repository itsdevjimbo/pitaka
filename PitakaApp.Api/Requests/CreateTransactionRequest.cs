using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Requests;

public record CreateTransactionRequest (
    [Required]
    int AccountId,

    [Required]
    TransactionType Type,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,
    
    int? CategoryId,
    
    DateTime? TransactionDate,

    int? TransferToAccountId,

    string? Description
): IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {

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
    }

    public CreateTransactionInput ToInput(Account account) =>
        new (
            Account: account, 
            Type: Type, 
            Amount: Amount,
            TransactionDate: TransactionDate,
            CategoryId: CategoryId,
            TransferToAccountId: TransferToAccountId,
            Description: Description
        );
}