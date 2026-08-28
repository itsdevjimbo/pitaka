using System.ComponentModel.DataAnnotations;

namespace PitakaApp.Api.Options;

public class CorsOption
{
    public const string SectionName = "Cors";

    // The browser origins allowed to call this API. Each entry must be a bare
    // scheme-and-authority origin (e.g. "http://localhost:4200") — a trailing
    // slash or a path will never match a browser's Origin header, and a policy
    // that matches nothing is indistinguishable from having no policy at all.
    [Required(ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    public string[] AllowedOrigins { get; set; } = [];
}
