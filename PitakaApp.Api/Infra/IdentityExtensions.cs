using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Infra;

public static class IdentityExtensions
{
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
            .AddDefaultTokenProviders();

        // The default key ring lives at ~/.aspnet/DataProtection-Keys — per-machine, and
        // wiped on every container redeploy, which would take every outstanding
        // confirmation/reset token with it. Persisting to the database keeps the ring
        // (and the tokens it protects) alive across deploys. This is the hand-rolled
        // equivalent of AddDataProtection().PersistKeysToDbContext<PitakaDbContext>() —
        // see EfDataProtectionKeyRepository for why the official package isn't used.
        services.AddDataProtection();
        services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>((options, serviceProvider) =>
            {
                options.XmlRepository = new EfDataProtectionKeyRepository(serviceProvider);
            });

        // DataProtectorTokenProvider is the default provider AddDefaultTokenProviders
        // wires for password-reset, email-confirmation and change-email tokens — one
        // shared lifespan for all three until there is a reason to split them.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

        return services;
    }

    // Shared with UserFactory.BuildUserManager (the test suite's hand-built
    // UserManager<User>, needed because UserFactory.CreateAsync takes only a
    // PitakaDbContext across its ~25 call sites, not a service scope) so the real store
    // and the test store can't silently drift apart on what counts as a valid password.
    public static void ConfigureIdentityOptions(IdentityOptions options)
    {
        // S1 keeps behaviour byte-identical to today: every registered Profile could
        // sign in immediately. S2 flips this to true.
        options.SignIn.RequireConfirmedAccount = false;

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
    }
}
