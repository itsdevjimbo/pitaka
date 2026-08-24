using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record TagRequest (
    [Required, MaxLength(255)]
    string Name
)
{
    public TagInput ToInput() => new TagInput (Name);
}