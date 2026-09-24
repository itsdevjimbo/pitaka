using PitakaApp.Api.Services;

namespace PitakaApp.Api.Jobs;

public class ProfilePictureCleanupWorker(
    ILogger<ProfilePictureCleanupWorker> logger,
    IServiceScopeFactory scopeFactory
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ILogger<ProfilePictureCleanupWorker> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var cleanup =
                    scope.ServiceProvider.GetRequiredService<CleanupProfilePictureObjects>();
                await cleanup.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Profile picture cleanup run failed.");
            }
        }
    }
}
