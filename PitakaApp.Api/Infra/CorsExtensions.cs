using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Infra;

public static class CorsExtensions
{
    // Greppable on purpose: the evidence line for this gap was `grep "Cors"`
    // returning zero hits outside obj/.
    public const string PolicyName = "PitakaWeb";

    public static WebApplicationBuilder AddPitakaCors(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<CorsOption>()
            .Bind(builder.Configuration.GetSection(CorsOption.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                // Null is caught by [Required]; this validator also runs on failure, so no-op on null.
                option => option.AllowedOrigins?.All(IsSchemeAndAuthorityOrigin) ?? true,
                "Cors:AllowedOrigins entries must be scheme-and-authority origins with no trailing slash or path (e.g. http://localhost:4200).")
            .ValidateOnStart();

        // The policy's origins are pulled from the validated CorsOption at the point the
        // CORS policy is built (first request), not from a builder.Configuration read at
        // registration time — same reasoning as the JWT wiring in
        // JwtAuthenticationExtensions: a raw read here is too early to see configuration
        // sources the test suite adds via WebApplicationFactory.ConfigureAppConfiguration.
        builder.Services.AddCors();
        builder.Services.AddOptions<CorsOptions>()
            .Configure<IOptions<CorsOption>>((corsOptions, corsOption) =>
            {
                corsOptions.AddPolicy(PolicyName, policy => policy
                    .WithOrigins(corsOption.Value.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });

        return builder;
    }

    // A browser Origin header is always scheme://host[:port] with no path and no
    // trailing slash. Uri round-trips anything else to a different string.
    private static bool IsSchemeAndAuthorityOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.GetLeftPart(UriPartial.Authority) == origin;
}
