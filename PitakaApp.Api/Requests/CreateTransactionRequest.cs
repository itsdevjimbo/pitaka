using System.ComponentModel.DataAnnotations;
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
)
{
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