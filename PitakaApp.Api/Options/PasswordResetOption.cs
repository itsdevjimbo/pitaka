using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class PasswordResetOption
{
    public const string SectionName = "PasswordReset";

    // The client's reset screen, not an API route — the API does not hard-code the
    // client's routing. userId and token are appended as query parameters — same shape
    // as EmailConfirmationOption.ConfirmUrl. The default in appsettings.json is the
    // `ng serve` origin already in the CORS allow-list.
    [Required(ErrorMessage = "PasswordReset:ResetUrl must be set — the client reset screen the email links to.")]
    [Url(ErrorMessage = "PasswordReset:ResetUrl must be an absolute URL.")]
    public string ResetUrl { get; set; } = string.Empty;
}
