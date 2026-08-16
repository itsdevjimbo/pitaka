namespace PitakaApp.Api.Requests;

using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

public record BudgetRequest (
    [Required, MaxLength(255)]
    string Name, 

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal AmountLimit,
    
    [Required]
    BudgetPeriod Period,

    [Required]
    DateOnly StartDate,

    DateOnly? EndDate,

    int? CategoryId,

    string? Description
)
{
    public BudgetInput ToInput() =>
        new(CategoryId: CategoryId, Name: Name, AmountLimit: AmountLimit, Period: Period, StartDate: StartDate, EndDate: EndDate, Description: Description);
}