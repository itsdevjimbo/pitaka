using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record GoalRequest (
    [Required, MaxLength(255)]
    string Name,

    [Required, Range(typeof(decimal), "0.01", "999999999999.99")]
    decimal TargetAmount,
    
    DateOnly? TargetDate = null
)
{
    public GoalInput ToInput() =>
        new(Name, TargetAmount, TargetDate);
}