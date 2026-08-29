using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class PasswordResetOption
{
    public const string SectionName = "PasswordReset";

    // Added to the injected TimeProvider's now to get a token's ExpiresAt. Defaults to
    // one hour; shortening it is a settings change, not a deployment. Validated > zero
    // on start.
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    // The client's reset screen, not an API route — the API does not hard-code the
    // client's routing. The token is appended as a ?token= query parameter. The default
    // in appsettings.json is the `ng serve` origin already in the CORS allow-list.
    [Required(ErrorMessage = "PasswordReset:ResetUrl must be set — the client reset screen the email links to.")]
    [Url(ErrorMessage = "PasswordReset:ResetUrl must be an absolute URL.")]
    public string ResetUrl { get; set; } = string.Empty;
}
