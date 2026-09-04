using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UpdateRecurringTransactionRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal Amount,

    int? CategoryId = null,

    string? Description = null,

    DateOnly? EndDate = null
)
{
    public UpdateRecurringTransactionInput ToInput() =>
        new (Name, Amount, CategoryId, Description, EndDate);
}