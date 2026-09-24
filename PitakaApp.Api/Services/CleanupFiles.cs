using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class CleanupFiles(
    PitakaDbContext context,
    IFileStorage storage,
    TimeProvider timeProvider,
    ILogger<CleanupFiles> logger
)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan AbandonedUploadAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DeletionLease = TimeSpan.FromMinutes(2);

    private readonly PitakaDbContext _context = context;
    private readonly IFileStorage _storage = storage;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<CleanupFiles> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // An upload intent is saved before the object-store write. If the process dies
        // before the file becomes available, this age-based transition makes it
        // eligible for deletion. The state predicate prevents cleanup from moving a
        // concurrently completed upload backwards.
        await _context
            .Files.Where(file =>
                file.State == StoredFileState.Uploading
                && file.CreatedAt <= now.Subtract(AbandonedUploadAge)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(file => file.State, StoredFileState.PendingDeletion)
                        .SetProperty(file => file.NextAttemptAt, now),
                cancellationToken
            );

        var files = await _context
            .Files.AsNoTracking()
            .Where(file =>
                (
                    file.State == StoredFileState.PendingDeletion
                    && (file.NextAttemptAt == null || file.NextAttemptAt <= now)
                )
                || (
                    file.State == StoredFileState.Deleting
                    && (file.DeletionLeaseUntil == null || file.DeletionLeaseUntil <= now)
                )
            )
            .OrderBy(file => file.CreatedAt)
            .Select(file => new { file.Id, file.ObjectKey })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            await DeleteIfClaimedAsync(file.Id, file.ObjectKey, now, cancellationToken);
        }
    }

    private async Task DeleteIfClaimedAsync(
        int fileId,
        string objectKey,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var leaseToken = Guid.NewGuid().ToString("N");
        var leaseUntil = now.Add(DeletionLease);
        var claimed = await _context
            .Files.Where(file =>
                file.Id == fileId
                && (
                    (
                        file.State == StoredFileState.PendingDeletion
                        && (file.NextAttemptAt == null || file.NextAttemptAt <= now)
                    )
                    || (
                        file.State == StoredFileState.Deleting
                        && (file.DeletionLeaseUntil == null || file.DeletionLeaseUntil <= now)
                    )
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(file => file.State, StoredFileState.Deleting)
                        .SetProperty(file => file.DeletionLeaseUntil, leaseUntil)
                        .SetProperty(file => file.DeletionLeaseToken, leaseToken),
                cancellationToken
            );

        if (claimed == 0)
        {
            return;
        }

        try
        {
            await _storage.DeleteAsync(objectKey, cancellationToken);
            await _context
                .Files.Where(file =>
                    file.Id == fileId
                    && file.State == StoredFileState.Deleting
                    && file.DeletionLeaseToken == leaseToken
                )
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            var attempts = await _context
                .Files.AsNoTracking()
                .Where(file => file.Id == fileId && file.DeletionLeaseToken == leaseToken)
                .Select(file => file.DeletionAttempts)
                .SingleOrDefaultAsync(cancellationToken);
            var retryAt = now.Add(Backoff(attempts));
            await _context
                .Files.Where(file =>
                    file.Id == fileId
                    && file.State == StoredFileState.Deleting
                    && file.DeletionLeaseToken == leaseToken
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(file => file.State, StoredFileState.PendingDeletion)
                            .SetProperty(file => file.NextAttemptAt, retryAt)
                            .SetProperty(file => file.DeletionLeaseUntil, (DateTime?)null)
                            .SetProperty(file => file.DeletionLeaseToken, (string?)null)
                            .SetProperty(file => file.DeletionAttempts, attempts + 1),
                    cancellationToken
                );

            _logger.LogWarning(
                exception,
                "Deleting stored file {ObjectKey} failed; retry is scheduled for {RetryAt}.",
                objectKey,
                retryAt
            );
        }
    }

    private static TimeSpan Backoff(int previousAttempts)
    {
        var minutes = Math.Min(60, 1 << Math.Min(previousAttempts, 6));
        return TimeSpan.FromMinutes(minutes);
    }
}
