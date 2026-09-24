using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public sealed record ProfilePictureContent(byte[] Content, string MediaType);

public class ProfilePictureService(
    PitakaDbContext context,
    IProfilePictureStorage storage,
    ProfilePictureImageProcessor imageProcessor,
    TimeProvider timeProvider,
    ILogger<ProfilePictureService> logger
)
{
    private const int MaxCommitAttempts = 5;
    private static readonly TimeSpan AbandonedUploadGracePeriod = TimeSpan.FromMinutes(2);

    private readonly PitakaDbContext _context = context;
    private readonly IProfilePictureStorage _storage = storage;
    private readonly ProfilePictureImageProcessor _imageProcessor = imageProcessor;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<ProfilePictureService> _logger = logger;

    public async Task<ProfilePictureValidationError?> UploadAsync(
        int userId,
        Stream source,
        long declaredLength,
        CancellationToken cancellationToken
    )
    {
        if (declaredLength == 0)
        {
            return ProfilePictureValidationError.Empty;
        }

        if (declaredLength > ProfilePictureImageProcessor.MaxEncodedBytes)
        {
            return ProfilePictureValidationError.TooLarge;
        }

        var bytes = await ReadBoundedAsync(source, cancellationToken);
        if (bytes is null)
        {
            return ProfilePictureValidationError.TooLarge;
        }

        var processed = _imageProcessor.Process(bytes);
        if (processed.Error is { } imageError)
        {
            return imageError;
        }

        var picture = processed.Picture!;
        var objectKey = $"profile-pictures/{Guid.NewGuid():N}";
        _context.ProfilePictureObjects.Add(
            new ProfilePictureObject
            {
                ObjectKey = objectKey,
                State = ProfilePictureObjectState.Uploading,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _storage.PutAsync(
                objectKey,
                picture.Content,
                picture.MediaType,
                cancellationToken
            );
            await MakeCurrentAsync(userId, objectKey, picture.MediaType, cancellationToken);
            return null;
        }
        catch
        {
            await MarkForCleanupAsync(objectKey);
            throw;
        }
    }

    public async Task<ProfilePictureContent?> GetCurrentAsync(
        int userId,
        CancellationToken cancellationToken
    )
    {
        var reference = await _context
            .Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.ProfilePictureObjectKey, user.ProfilePictureMediaType })
            .SingleOrDefaultAsync(cancellationToken);

        if (reference?.ProfilePictureObjectKey is not { } objectKey)
        {
            return null;
        }

        if (reference.ProfilePictureMediaType is not { } mediaType)
        {
            throw new InvalidOperationException("The Profile picture media type is missing.");
        }

        var content = await _storage.GetAsync(objectKey, cancellationToken);
        return new ProfilePictureContent(content, mediaType);
    }

    public async Task RemoveAsync(int userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCommitAttempts; attempt++)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                cancellationToken
            );

            try
            {
                var user = await _context.Users.SingleOrDefaultAsync(
                    candidate => candidate.Id == userId,
                    cancellationToken
                );

                if (user is null || user.ProfilePictureObjectKey is not { } objectKey)
                {
                    return;
                }

                var picture = await _context.ProfilePictureObjects.SingleOrDefaultAsync(
                    candidate => candidate.ObjectKey == objectKey,
                    cancellationToken
                );
                if (picture is null || picture.State != ProfilePictureObjectState.Current)
                {
                    throw new InvalidOperationException(
                        $"Current Profile picture object {objectKey} has no current lifecycle record."
                    );
                }

                user.ProfilePictureObjectKey = null;
                user.ProfilePictureMediaType = null;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                picture.State = ProfilePictureObjectState.PendingDeletion;
                picture.NextAttemptAt = _timeProvider.GetUtcNow().UtcDateTime;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaxCommitAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
            }
        }
    }

    private async Task MakeCurrentAsync(
        int userId,
        string objectKey,
        string mediaType,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < MaxCommitAttempts; attempt++)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                cancellationToken
            );

            try
            {
                var user =
                    await _context.Users.SingleOrDefaultAsync(
                        candidate => candidate.Id == userId,
                        cancellationToken
                    ) ?? throw new InvalidOperationException($"Profile {userId} no longer exists.");
                var newPicture = await _context.ProfilePictureObjects.SingleAsync(
                    candidate => candidate.ObjectKey == objectKey,
                    cancellationToken
                );
                if (newPicture.State != ProfilePictureObjectState.Uploading)
                {
                    throw new InvalidOperationException(
                        $"Profile picture object {objectKey} was claimed for cleanup before commit."
                    );
                }

                if (user.ProfilePictureObjectKey is { } oldObjectKey)
                {
                    var oldPicture = await _context.ProfilePictureObjects.SingleOrDefaultAsync(
                        candidate => candidate.ObjectKey == oldObjectKey,
                        cancellationToken
                    );
                    if (oldPicture is null || oldPicture.State != ProfilePictureObjectState.Current)
                    {
                        throw new InvalidOperationException(
                            $"Current Profile picture object {oldObjectKey} has no current lifecycle record."
                        );
                    }

                    oldPicture.State = ProfilePictureObjectState.PendingDeletion;
                    oldPicture.NextAttemptAt = _timeProvider.GetUtcNow().UtcDateTime;
                }

                user.ProfilePictureObjectKey = objectKey;
                user.ProfilePictureMediaType = mediaType;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                newPicture.State = ProfilePictureObjectState.Current;
                newPicture.NextAttemptAt = null;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaxCommitAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
            }
        }
    }

    private async Task MarkForCleanupAsync(string objectKey)
    {
        try
        {
            _context.ChangeTracker.Clear();
            var picture = await _context.ProfilePictureObjects.SingleOrDefaultAsync(candidate =>
                candidate.ObjectKey == objectKey
            );

            // A commit outcome can be uncertain if the connection fails while committing.
            // Only an upload intent can be queued here; a picture that actually committed
            // current remains protected from cleanup.
            if (picture?.State != ProfilePictureObjectState.Uploading)
            {
                return;
            }

            picture.State = ProfilePictureObjectState.PendingDeletion;
            picture.NextAttemptAt = _timeProvider
                .GetUtcNow()
                .UtcDateTime.Add(AbandonedUploadGracePeriod);
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not queue Profile picture object {ObjectKey} for cleanup; its upload intent remains durable.",
                objectKey
            );
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken
    )
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > ProfilePictureImageProcessor.MaxEncodedBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }
}
