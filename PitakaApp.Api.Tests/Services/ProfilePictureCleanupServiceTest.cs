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
public class ProfilePictureCleanupServiceTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly CleanupProfilePictureObjects _cleanup;
    private readonly InMemoryProfilePictureStorage _storage;
    private readonly FakeTimeProvider _clock;

    public ProfilePictureCleanupServiceTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _storage = factory.PictureStorage;
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        _cleanup = new CleanupProfilePictureObjects(
            _context,
            _storage,
            _clock,
            _scope.ServiceProvider.GetRequiredService<ILogger<CleanupProfilePictureObjects>>()
        );
        _storage.DeleteFailure = null;
    }

    [Fact]
    public async Task Cleanup_DeletesAbandonedObjects_RetriesFailures_AndPreservesCurrentPicture()
    {
        var user = await UserFactory.CreateAsync(_context);
        var currentKey = $"profile-pictures/{Guid.NewGuid():N}";
        var abandonedKey = $"profile-pictures/{Guid.NewGuid():N}";
        var retryKey = $"profile-pictures/{Guid.NewGuid():N}";
        var now = _clock.GetUtcNow().UtcDateTime;

        user.ProfilePictureObjectKey = currentKey;
        user.ProfilePictureMediaType = "image/png";
        _context.ProfilePictureObjects.AddRange(
            new ProfilePictureObject
            {
                ObjectKey = currentKey,
                State = ProfilePictureObjectState.Current,
                CreatedAt = now,
            },
            new ProfilePictureObject
            {
                ObjectKey = abandonedKey,
                State = ProfilePictureObjectState.Uploading,
                CreatedAt = now.AddHours(-1),
            },
            new ProfilePictureObject
            {
                ObjectKey = retryKey,
                State = ProfilePictureObjectState.PendingDeletion,
                CreatedAt = now.AddHours(-1),
                NextAttemptAt = now.AddMinutes(-1),
            }
        );
        await _context.SaveChangesAsync();

        _storage.Store(currentKey, [1]);
        _storage.Store(abandonedKey, [2]);
        _storage.Store(retryKey, [3]);
        _storage.DeleteFailure = new IOException("temporary object-store failure");

        await _cleanup.RunAsync(CancellationToken.None);

        _context.ChangeTracker.Clear();
        var failedAttempts = await _context
            .ProfilePictureObjects.AsNoTracking()
            .Where(picture => picture.ObjectKey == abandonedKey || picture.ObjectKey == retryKey)
            .ToListAsync();
        Assert.Equal(2, failedAttempts.Count);
        Assert.All(
            failedAttempts,
            picture =>
            {
                Assert.Equal(ProfilePictureObjectState.PendingDeletion, picture.State);
                Assert.Equal(1, picture.DeletionAttempts);
                Assert.Equal(now.AddMinutes(1), picture.NextAttemptAt);
            }
        );
        Assert.True(_storage.Contains(currentKey));
        Assert.DoesNotContain(currentKey, _storage.DeletedKeys);

        _storage.DeleteFailure = null;
        var retryAt = now.AddMinutes(-1);
        await _context
            .ProfilePictureObjects.Where(picture =>
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
            await _context.ProfilePictureObjects.AnyAsync(picture =>
                picture.ObjectKey == currentKey
                && picture.State == ProfilePictureObjectState.Current
            )
        );
    }

    public void Dispose() => _scope.Dispose();
}
