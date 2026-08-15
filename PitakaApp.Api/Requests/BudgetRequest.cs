namespace PitakaApp.Api.Requests;

using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;

public record BudgetRequest (
    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal AmountLimit,
    
    [Required]
    BudgetPeriod Period,

    [Required]
    DateOnly StartDate,

    DateOnly? EndDate,

    int? CategoryId
)
{
    public BudgetInput ToInput() =>
        new(CategoryId: CategoryId, AmountLimit: AmountLimit, Period: Period, StartDate: StartDate, EndDate: EndDate);
}