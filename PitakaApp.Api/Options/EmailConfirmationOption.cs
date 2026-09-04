using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class EmailConfirmationOption
{
    public const string SectionName = "EmailConfirmation";

    // The client's confirm-email landing page, not an API route — same shape as
    // PasswordResetOption.ResetUrl. userId and token are appended as query parameters.
    // The default in appsettings.json is the `ng serve` origin already in the CORS
    // allow-list.
    [Required(ErrorMessage = "EmailConfirmation:ConfirmUrl must be set — the client confirm-email screen the email links to.")]
    [Url(ErrorMessage = "EmailConfirmation:ConfirmUrl must be an absolute URL.")]
    public string ConfirmUrl { get; set; } = string.Empty;
}
