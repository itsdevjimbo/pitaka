using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class CleanupProfilePictureObjects(
    PitakaDbContext context,
    IProfilePictureStorage storage,
    TimeProvider timeProvider,
    ILogger<CleanupProfilePictureObjects> logger
)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan AbandonedUploadAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DeletionLease = TimeSpan.FromMinutes(2);

    private readonly PitakaDbContext _context = context;
    private readonly IProfilePictureStorage _storage = storage;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<CleanupProfilePictureObjects> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // An upload intent is saved before the S3 write. If the process dies at any
        // point before the Profile transaction, this age-based transition makes it
        // eligible for deletion. The state predicate is also the guard that prevents
        // cleanup from moving a concurrently committed current picture backwards.
        await _context
            .ProfilePictureObjects.Where(picture =>
                picture.State == ProfilePictureObjectState.Uploading
                && picture.CreatedAt <= now.Subtract(AbandonedUploadAge)
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            picture => picture.State,
                            ProfilePictureObjectState.PendingDeletion
                        )
                        .SetProperty(picture => picture.NextAttemptAt, now),
                cancellationToken
            );

        var objectKeys = await _context
            .ProfilePictureObjects.AsNoTracking()
            .Where(picture =>
                (
                    picture.State == ProfilePictureObjectState.PendingDeletion
                    && (picture.NextAttemptAt == null || picture.NextAttemptAt <= now)
                )
                || (
                    picture.State == ProfilePictureObjectState.Deleting
                    && (picture.DeletionLeaseUntil == null || picture.DeletionLeaseUntil <= now)
                )
            )
            .OrderBy(picture => picture.CreatedAt)
            .Select(picture => picture.ObjectKey)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var objectKey in objectKeys)
        {
            await DeleteIfClaimedAsync(objectKey, now, cancellationToken);
        }
    }

    private async Task DeleteIfClaimedAsync(
        string objectKey,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var leaseToken = Guid.NewGuid().ToString("N");
        var leaseUntil = now.Add(DeletionLease);
        var claimed = await _context
            .ProfilePictureObjects.Where(picture =>
                picture.ObjectKey == objectKey
                && (
                    (
                        picture.State == ProfilePictureObjectState.PendingDeletion
                        && (picture.NextAttemptAt == null || picture.NextAttemptAt <= now)
                    )
                    || (
                        picture.State == ProfilePictureObjectState.Deleting
                        && (picture.DeletionLeaseUntil == null || picture.DeletionLeaseUntil <= now)
                    )
                )
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(picture => picture.State, ProfilePictureObjectState.Deleting)
                        .SetProperty(picture => picture.DeletionLeaseUntil, leaseUntil)
                        .SetProperty(picture => picture.DeletionLeaseToken, leaseToken),
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
                .ProfilePictureObjects.Where(picture =>
                    picture.ObjectKey == objectKey
                    && picture.State == ProfilePictureObjectState.Deleting
                    && picture.DeletionLeaseToken == leaseToken
                )
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            var attempts = await _context
                .ProfilePictureObjects.AsNoTracking()
                .Where(picture =>
                    picture.ObjectKey == objectKey && picture.DeletionLeaseToken == leaseToken
                )
                .Select(picture => picture.DeletionAttempts)
                .SingleOrDefaultAsync(cancellationToken);
            var retryAt = now.Add(Backoff(attempts));
            await _context
                .ProfilePictureObjects.Where(picture =>
                    picture.ObjectKey == objectKey
                    && picture.State == ProfilePictureObjectState.Deleting
                    && picture.DeletionLeaseToken == leaseToken
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                picture => picture.State,
                                ProfilePictureObjectState.PendingDeletion
                            )
                            .SetProperty(picture => picture.NextAttemptAt, retryAt)
                            .SetProperty(picture => picture.DeletionLeaseUntil, (DateTime?)null)
                            .SetProperty(picture => picture.DeletionLeaseToken, (string?)null)
                            .SetProperty(picture => picture.DeletionAttempts, attempts + 1),
                    cancellationToken
                );

            _logger.LogWarning(
                exception,
                "Deleting Profile picture object {ObjectKey} failed; retry is scheduled for {RetryAt}.",
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
