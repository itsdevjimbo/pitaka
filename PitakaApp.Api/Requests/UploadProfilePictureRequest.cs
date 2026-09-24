using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using PitakaApp.Api.Inputs;

namespace PitakaApp.Api.Requests;

public record UploadProfilePictureRequest([Required] IFormFile? File)
{
    public UploadProfilePictureInput ToInput()
    {
        var file = File ?? throw new InvalidOperationException("A picture file is required.");
        return new UploadProfilePictureInput(file.OpenReadStream(), file.Length);
    }
}
