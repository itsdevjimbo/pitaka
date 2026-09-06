using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Infra;

// A DataProtector token provider that is identical to Identity's default in every way
// except that it reads its lifespan from its own options type. AddDefaultTokenProviders
// points password-reset, email-confirmation and change-email tokens all at the one
// "Default" provider and its single DataProtectionTokenProviderOptions.TokenLifespan;
// the change-email link needs a lifespan that moves independently (ADR 0014), and a
// distinct options type is the only way DataProtectorTokenProvider takes one.
//
// IOptions<T> is covariant, so IOptions<ChangeEmailTokenProviderOptions> satisfies the
// base constructor's IOptions<DataProtectionTokenProviderOptions>.
public class ChangeEmailTokenProvider : DataProtectorTokenProvider<User>
{
    public ChangeEmailTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ChangeEmailTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<User>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}

public class ChangeEmailTokenProviderOptions : DataProtectionTokenProviderOptions
{
    // One string with two jobs: the DI key AddTokenProvider registers this provider
    // under (and IdentityOptions.Tokens.ChangeEmailTokenProvider points at), and — as
    // the base Name — part of the DataProtector purpose the token is bound to. Both must
    // agree, so there is only the one.
    public const string ProviderName = "PitakaChangeEmail";

    public ChangeEmailTokenProviderOptions()
    {
        Name = ProviderName;
    }
}
