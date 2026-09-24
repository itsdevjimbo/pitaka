using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public sealed record ProfilePictureContent(byte[] Content, string MediaType);

public class ProfilePictureService(
    PitakaDbContext context,
    IFileStorage storage,
    ProfilePictureImageProcessor imageProcessor,
    TimeProvider timeProvider,
    ILogger<ProfilePictureService> logger
)
{
    private const int MaxCommitAttempts = 5;
    private static readonly TimeSpan AbandonedUploadGracePeriod = TimeSpan.FromMinutes(2);

    private readonly PitakaDbContext _context = context;
    private readonly IFileStorage _storage = storage;
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
        var file = new StoredFile
        {
            ObjectKey = $"files/{Guid.NewGuid():N}",
            MediaType = picture.MediaType,
            State = StoredFileState.Uploading,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _context.Files.Add(file);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _storage.PutAsync(
                file.ObjectKey,
                picture.Content,
                picture.MediaType,
                cancellationToken
            );
            await MakeCurrentAsync(userId, file.Id, cancellationToken);
            return null;
        }
        catch
        {
            await MarkForCleanupAsync(file.Id);
            throw;
        }
    }

    public async Task<ProfilePictureContent?> GetCurrentAsync(
        int userId,
        CancellationToken cancellationToken
    )
    {
        var file = await _context
            .Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Photo)
            .SingleOrDefaultAsync(cancellationToken);

        if (file is null)
        {
            return null;
        }

        var content = await _storage.GetAsync(file.ObjectKey, cancellationToken);
        return new ProfilePictureContent(content, file.MediaType);
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

                if (user is null || user.PhotoId is not { } fileId)
                {
                    return;
                }

                var file = await _context.Files.SingleOrDefaultAsync(
                    candidate => candidate.Id == fileId,
                    cancellationToken
                );
                if (file is null || file.State != StoredFileState.Available)
                {
                    throw new InvalidOperationException(
                        $"Current Profile picture file {fileId} has no current lifecycle record."
                    );
                }

                user.PhotoId = null;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                file.State = StoredFileState.PendingDeletion;
                file.NextAttemptAt = _timeProvider.GetUtcNow().UtcDateTime;

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

    private async Task MakeCurrentAsync(int userId, int fileId, CancellationToken cancellationToken)
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
                var newFile = await _context.Files.SingleAsync(
                    candidate => candidate.Id == fileId,
                    cancellationToken
                );
                if (newFile.State != StoredFileState.Uploading)
                {
                    throw new InvalidOperationException(
                        $"Profile picture file {fileId} was claimed for cleanup before commit."
                    );
                }

                if (user.PhotoId is { } oldFileId)
                {
                    var oldFile = await _context.Files.SingleOrDefaultAsync(
                        candidate => candidate.Id == oldFileId,
                        cancellationToken
                    );
                    if (oldFile is null || oldFile.State != StoredFileState.Available)
                    {
                        throw new InvalidOperationException(
                            $"Current Profile picture file {oldFileId} has no current lifecycle record."
                        );
                    }

                    oldFile.State = StoredFileState.PendingDeletion;
                    oldFile.NextAttemptAt = _timeProvider.GetUtcNow().UtcDateTime;
                }

                user.PhotoId = newFile.Id;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                newFile.State = StoredFileState.Available;
                newFile.NextAttemptAt = null;

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

    private async Task MarkForCleanupAsync(int fileId)
    {
        try
        {
            _context.ChangeTracker.Clear();
            var file = await _context.Files.SingleOrDefaultAsync(candidate =>
                candidate.Id == fileId
            );

            // A commit outcome can be uncertain if the connection fails while committing.
            // Only an upload intent can be queued here; a picture that actually committed
            // current remains protected from cleanup.
            if (file?.State != StoredFileState.Uploading)
            {
                return;
            }

            file.State = StoredFileState.PendingDeletion;
            file.NextAttemptAt = _timeProvider
                .GetUtcNow()
                .UtcDateTime.Add(AbandonedUploadGracePeriod);
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not queue Profile picture file {FileId} for cleanup; its upload intent remains durable.",
                fileId
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
