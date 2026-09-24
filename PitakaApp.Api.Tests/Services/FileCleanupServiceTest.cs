using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public class FileCleanupServiceTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly CleanupFiles _cleanup;
    private readonly InMemoryFileStorage _storage;
    private readonly FakeTimeProvider _clock;

    public FileCleanupServiceTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _storage = factory.FileStorage;
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        _cleanup = new CleanupFiles(
            _context,
            _storage,
            _clock,
            _scope.ServiceProvider.GetRequiredService<ILogger<CleanupFiles>>()
        );
        _storage.DeleteFailure = null;
    }

    [Fact]
    public async Task Cleanup_DeletesAbandonedFiles_RetriesFailures_AndPreservesReferencedFile()
    {
        var user = await UserFactory.CreateAsync(_context);
        var currentKey = $"files/{Guid.NewGuid():N}";
        var abandonedKey = $"files/{Guid.NewGuid():N}";
        var retryKey = $"files/{Guid.NewGuid():N}";
        var now = _clock.GetUtcNow().UtcDateTime;

        var currentFile = await StoredFileFactory.CreateAsync(
            _context,
            currentKey,
            "image/png",
            StoredFileState.PendingDeletion,
            now,
            now
        );
        user.PhotoId = currentFile.Id;
        await _context.SaveChangesAsync();
        await StoredFileFactory.CreateAsync(
            _context,
            abandonedKey,
            "image/png",
            StoredFileState.Uploading,
            now.AddHours(-1)
        );
        await StoredFileFactory.CreateAsync(
            _context,
            retryKey,
            "image/png",
            StoredFileState.PendingDeletion,
            now.AddHours(-1),
            now.AddMinutes(-1)
        );

        _storage.Store(currentKey, [1]);
        _storage.Store(abandonedKey, [2]);
        _storage.Store(retryKey, [3]);
        _storage.DeleteFailure = new IOException("temporary object-store failure");

        await _cleanup.RunAsync(CancellationToken.None);

        _context.ChangeTracker.Clear();
        var failedAttempts = await _context
            .Files.AsNoTracking()
            .Where(picture => picture.ObjectKey == abandonedKey || picture.ObjectKey == retryKey)
            .ToListAsync();
        Assert.Equal(2, failedAttempts.Count);
        Assert.All(
            failedAttempts,
            picture =>
            {
                Assert.Equal(StoredFileState.PendingDeletion, picture.State);
                Assert.Equal(1, picture.DeletionAttempts);
                Assert.Equal(now.AddMinutes(1), picture.NextAttemptAt);
            }
        );
        Assert.True(_storage.Contains(currentKey));
        Assert.DoesNotContain(currentKey, _storage.DeletedKeys);
        Assert.True(
            await _context.Files.AnyAsync(picture =>
                picture.ObjectKey == currentKey && picture.State == StoredFileState.PendingDeletion
            )
        );

        _storage.DeleteFailure = null;
        var retryAt = now.AddMinutes(-1);
        await _context
            .Files.Where(picture =>
                picture.ObjectKey == abandonedKey || picture.ObjectKey == retryKey
            )
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(picture => picture.NextAttemptAt, retryAt)
            );

        await _cleanup.RunAsync(CancellationToken.None);

        Assert.False(_storage.Contains(abandonedKey));
        Assert.False(_storage.Contains(retryKey));
        Assert.True(_storage.Contains(currentKey));
        _context.ChangeTracker.Clear();
        Assert.True(
            await _context.Files.AnyAsync(picture =>
                picture.ObjectKey == currentKey && picture.State == StoredFileState.PendingDeletion
            )
        );
    }

    public void Dispose() => _scope.Dispose();
}
