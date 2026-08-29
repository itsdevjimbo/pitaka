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

        builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

        return builder;
    }
}
