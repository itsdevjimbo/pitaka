using Microsoft.Extensions.Options;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Jobs;

public class RecurringTransactionGenerationWorker(
    ILogger<RecurringTransactionGenerationWorker> logger,
    IOptions<RecurringTransactionGenerationOption> recurringTransactionGenerationOption,
    IServiceScopeFactory scopeFactory
) : BackgroundService
{
    private readonly ILogger<RecurringTransactionGenerationWorker> _logger = logger;

    private readonly RecurringTransactionGenerationOption _recurringTransactionGenerationOption =
        recurringTransactionGenerationOption.Value;

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_recurringTransactionGenerationOption.Enabled)
        {
            _logger.LogInformation("Recurring transaction generation is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_recurringTransactionGenerationOption.Interval);

        do
        {
            _logger.LogInformation("Service triggered at: {time}", DateTime.UtcNow);

            using var scope = _scopeFactory.CreateScope();
            try
            {
                var generateDueRecurringTransactions =
                    scope.ServiceProvider.GetRequiredService<GenerateDueRecurringTransactions>();
                await generateDueRecurringTransactions.GenerateAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring transaction generation run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
