using System.ComponentModel.DataAnnotations;
using PitakaApp.Api.Infra;

namespace PitakaApp.Api.Options;

public class EmailChangeOption
{
    public const string SectionName = "EmailChange";

    // The client's confirm-email-change landing page, not an API route — same shape as
    // EmailConfirmationOption.ConfirmUrl. userId and token are appended as query
    // parameters. The default in appsettings.json is the `ng serve` origin already in
    // the CORS allow-list.
    [Required(ErrorMessage = "EmailChange:ConfirmUrl must be set — the client confirm-email-change screen the email links to.")]
    [Url(ErrorMessage = "EmailChange:ConfirmUrl must be an absolute URL.")]
    public string ConfirmUrl { get; set; } = string.Empty;

    // One lifespan for both the confirmation token and the stored pending expiry, so the
    // link and the state it points at die together (ADR 0014). Its own option rather
    // than the shared DataProtectionTokenProviderOptions.TokenLifespan that reset and
    // registration-confirmation ride — the split IdentityExtensions anticipated. Defaults
    // by reference to the registration confirmation lifespan, not a copy of its value.
    public TimeSpan TokenLifespan { get; set; } = IdentityExtensions.RegistrationConfirmationTokenLifespan;
}
