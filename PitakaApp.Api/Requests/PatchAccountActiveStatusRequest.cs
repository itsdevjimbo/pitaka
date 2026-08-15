using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record PatchAccountActiveStatusRequest (
    [Required]
    bool IsActive
)
{
    public PatchAccountActiveInput ToInput() => new(IsActive: IsActive);
}