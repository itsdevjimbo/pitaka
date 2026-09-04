using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record PatchCategoryActiveStatusRequest(
    [Required]
    bool IsActive
)
{
    public PatchCategoryActiveInput ToInput() => new(IsActive: IsActive);
}
