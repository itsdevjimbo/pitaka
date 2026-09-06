using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Infra;

public static class IdentityExtensions
{
    // The lifespan of the password-reset and email-confirmation tokens (all three of
    // Identity's DataProtector tokens, until the change-email one below was split off).
    // Exposed so EmailChangeOption can default to it by reference rather than by copying
    // the literal — "defaults to the registration confirmation lifespan" (ADR 0014) then
    // stays true if this value ever moves.
    public static readonly TimeSpan RegistrationConfirmationTokenLifespan = TimeSpan.FromHours(1);

    // AddIdentityCore, not AddIdentity — AddIdentity registers the application/external
    // cookie schemes and sets a cookie scheme as the authentication default, which would
    // take [Authorize]'s default resolution away from JwtBearer. AddIdentityCore +
    // AddSignInManager gives the store, the password hasher/validators and the sign-in
    // checks without touching authentication. This is the single choice
    // most likely to be "fixed" into a regression by a later contributor.
    public static IServiceCollection AddPitakaIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<User>(ConfigureIdentityOptions)
            .AddSignInManager()
            .AddEntityFrameworkStores<PitakaDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<ChangeEmailTokenProvider>(ChangeEmailTokenProviderOptions.ProviderName);

        // Route only the change-email token through the provider above. Set here rather
        // than in ConfigureIdentityOptions because that method is shared with
        // UserFactory.BuildUserManager, whose hand-built UserManager registers no
        // providers — pointing it at a name it cannot resolve would be a trap for a
        // future test that reaches GenerateChangeEmailTokenAsync through it.
        services.Configure<IdentityOptions>(o =>
            o.Tokens.ChangeEmailTokenProvider = ChangeEmailTokenProviderOptions.ProviderName);

        // The change-email link's lifespan is EmailChangeOption's to set (ADR 0014), not
        // the shared one below. Lazy Configure — same shape as JwtBearerOptions reading
        // IOptions<JwtOption> — so it does not matter that AddEmailSender binds
        // EmailChangeOption after this runs.
        services.AddOptions<ChangeEmailTokenProviderOptions>()
            .Configure<IOptions<EmailChangeOption>>((tokenOptions, emailChange) =>
                tokenOptions.TokenLifespan = emailChange.Value.TokenLifespan);

        // The default key ring lives at ~/.aspnet/DataProtection-Keys — per-machine, and
        // wiped on every container redeploy, which would take every outstanding
        // confirmation/reset token with it. Persisting to the database keeps the ring
        // (and the tokens it protects) alive across deploys.
        services.AddDataProtection().PersistKeysToDbContext<PitakaDbContext>();

        // DataProtectorTokenProvider is the default provider AddDefaultTokenProviders
        // wires for password-reset and email-confirmation tokens (change-email now has
        // its own, above). ADR 0014 was the reason to split.
        services.Configure<DataProtectionTokenProviderOptions>(o =>
            o.TokenLifespan = RegistrationConfirmationTokenLifespan);

        return services;
    }

    // Shared with UserFactory.BuildUserManager (the test suite's hand-built
    // UserManager<User>, needed because UserFactory.CreateAsync takes only a
    // PitakaDbContext across its ~25 call sites, not a service scope) so the real store
    // and the test store can't silently drift apart on what counts as a valid password.
    public static void ConfigureIdentityOptions(IdentityOptions options)
    {
        // S2: a Profile must confirm its email before CheckPasswordSignInAsync will
        // succeed for it — an otherwise-correct sign-in comes back IsNotAllowed instead.
        // S1 kept this false to stay byte-identical to pre-Identity behaviour.
        options.SignIn.RequireConfirmedAccount = true;

        // The exact length-only rule PasswordRules already expresses — the store and
        // the request-edge [StringLength] agree on what a valid password is.
        options.Password.RequiredLength = PasswordRules.MinLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        // Identity's UserNameIndex/EmailIndex (unique because of this) replace the
        // hand-rolled unique index on Email. Lookups go through FindByEmailAsync, which
        // hits the normalized column.
        options.User.RequireUniqueEmail = true;

        // UserName is set to Email and never surfaces (see AuthController). The default
        // charset rejects characters — an apostrophe, say — that [EmailAddress] already
        // let through at the request edge, which used to surface as "email already
        // registered" for an address that was never actually taken. Widened to the rest
        // of RFC 5322's atext so a legal email local part cannot trip this validator.
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+!#$%&'*/=?^`{|}~";
    }
}
