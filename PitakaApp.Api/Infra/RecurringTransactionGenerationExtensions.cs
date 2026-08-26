using PitakaApp.Api.Jobs;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Infra;

public static class RecurringTransactionGenerationExtensions
{
    public static WebApplicationBuilder AddRecurringTransactionGeneration(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<RecurringTransactionGenerationOption>()
            .Bind(builder.Configuration.GetSection(RecurringTransactionGenerationOption.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => !o.Enabled || o.Interval > TimeSpan.Zero) 
            .ValidateOnStart();

        builder.Services.AddHostedService<RecurringTransactionGenerationWorker>();

        return builder;
    }
}
