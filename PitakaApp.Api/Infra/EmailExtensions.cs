using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Infra;

public static class EmailExtensions
{
    // Binds the option and registers IEmailSender -> SmtpEmailSender in one
    // place, following AddRecurringTransactionGeneration's shape. The sender
    // lives under this extension rather than a general email module because
    // password reset is its only consumer; it splits out when something else
    // sends mail.
    public static WebApplicationBuilder AddEmailSender(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<EmailOption>()
            .Bind(builder.Configuration.GetSection(EmailOption.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Password reset is IEmailSender's only consumer, so its option is bound here
        // rather than in a module of its own. Validated on start like the JWT and CORS
        // settings: a missing reset URL fails on boot, not on the first person who
        // forgets their password.
        builder.Services.AddOptions<PasswordResetOption>()
            .Bind(builder.Configuration.GetSection(PasswordResetOption.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.TokenLifetime > TimeSpan.Zero, "PasswordReset:TokenLifetime must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

        return builder;
    }
}
