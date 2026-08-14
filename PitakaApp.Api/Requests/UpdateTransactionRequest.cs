using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateTransactionRequest (
    DateTime? TransactionDate,
    int? CategoryId,
    string? Description
)
{
    public UpdateTransactionInput ToInput() => new (
        TransactionDate: 
        TransactionDate,
        CategoryId: CategoryId,
        Description: Description
    );
}