using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;
using SkiaSharp;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class ProfilePictureControllerTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;
    private readonly InMemoryFileStorage _storage;

    public ProfilePictureControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
        _storage = factory.FileStorage;
        _storage.PutFailure = null;
        _storage.GetFailure = null;
        _storage.DeleteFailure = null;
        _storage.BeforePutAsync = null;
    }

    [Fact]
    public async Task PictureRoutes_RequireTheCurrentProfile_AndReturn404WhenNoPictureExists()
    {
        var anonymous = await _client.GetAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var absent = await _client.GetAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        var remove = await _client.DeleteAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
    }

    [Fact]
    public async Task UploadPicture_DetectsAndSanitizesTheBytes_AndEveryProfileResponseReportsPresence()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var source = ProfilePictureTestImages.AddExifMarker(
            ProfilePictureTestImages.Create(SKEncodedImageFormat.Jpeg, SKColors.DarkSlateBlue)
        );
        var upload = await PutPictureAsync(source, "misleading.png", "text/plain");
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        var profile = await ReadProfileAsync();
        Assert.True(profile.HasPicture);

        var picture = await _client.GetAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/jpeg", picture.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store, no-cache, max-age=0", picture.Headers.CacheControl!.ToString());
        Assert.Equal("no-cache", picture.Headers.Pragma.Single().Name);

        var bytes = await picture.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(bytes));
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);

        var nameUpdate = await _client.PutAsJsonAsync(
            "/api/profile",
            new { name = "Updated Name" }
        );
        Assert.Equal(HttpStatusCode.OK, nameUpdate.StatusCode);
        Assert.True((await nameUpdate.Content.ReadFromJsonAsync<ProfileResponse>())!.HasPicture);
    }

    [Fact]
    public async Task UploadPicture_RejectsMalformedUnsupportedAnimatedOversizedAndOverDimensionImages()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var cases = new (byte[] Bytes, string Error)[]
        {
            ([], "non-empty"),
            ([1, 2, 3, 4], "valid image"),
            (
                Convert.FromBase64String("R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="),
                "Only JPEG, PNG, and WebP"
            ),
            (ProfilePictureTestImages.CreateAnimatedWebp(), "Animated images"),
            (new byte[ProfilePictureImageProcessor.MaxEncodedBytes + 1], "2 MB or smaller"),
            (
                ProfilePictureTestImages.Create(
                    SKEncodedImageFormat.Png,
                    SKColors.CadetBlue,
                    4097,
                    1
                ),
                "4096 pixels or smaller"
            ),
        };

        foreach (var (bytes, expectedDetail) in cases)
        {
            var response = await PutPictureAsync(bytes, "anything.jpg", "image/jpeg");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Contains(expectedDetail, problem!.Detail);
        }

        var storedUser = await _context
            .Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        Assert.False(storedUser.HasPicture);
    }

    [Fact]
    public async Task UploadPicture_WhenReplacementIsInvalid_LeavesThePreviousPictureCurrent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var previousPicture = await UploadCurrentPngAsync(user.Id, SKColors.MediumSeaGreen);

        var rejected = await PutPictureAsync([1, 2, 3, 4], "invalid.png", "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await ReadProfileAsync()).HasPicture);
        Assert.Equal(previousPicture.Id, (await ReadCurrentStoredPictureAsync(user.Id)).Id);
        await AssertCurrentPngPictureColorAsync(SKColors.MediumSeaGreen);
    }

    [Fact]
    public async Task UploadPicture_WhenStorageWriteFails_LeavesThePreviousPictureCurrent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var previousPicture = await UploadCurrentPngAsync(user.Id, SKColors.MediumSeaGreen);

        _storage.PutFailure = new IOException("storage unavailable");
        try
        {
            var failedReplacement = await PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.Coral),
                "new.png",
                "image/png"
            );
            Assert.Equal(HttpStatusCode.InternalServerError, failedReplacement.StatusCode);

            var afterFailure = await _context
                .Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);
            Assert.Equal(previousPicture.Id, afterFailure.PhotoId);
            Assert.True(afterFailure.HasPicture);

            var read = await _client.GetAsync("/api/profile/picture");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(
                _storage.Read(previousPicture.ObjectKey),
                await read.Content.ReadAsByteArrayAsync()
            );
        }
        finally
        {
            _storage.PutFailure = null;
        }
    }

    [Fact]
    public async Task UploadPicture_WhenProfileCommitFails_LeavesThePreviousPictureCurrentAndCleansCandidate()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var previousPicture = await UploadCurrentPngAsync(user.Id, SKColors.MediumSeaGreen);
        var lastExistingFileId = await _context.Files.MaxAsync(file => (int?)file.Id) ?? 0;
        await _context.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS `fail_profile_picture_commit_test`"
        );
        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER `fail_profile_picture_commit_test` BEFORE UPDATE ON `users` FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced Profile picture commit failure'"
        );

        try
        {
            var failedReplacement = await PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.Coral),
                "replacement.png",
                "image/png"
            );
            Assert.Equal(HttpStatusCode.InternalServerError, failedReplacement.StatusCode);

            var afterFailure = await _context
                .Users.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);
            Assert.Equal(previousPicture.Id, afterFailure.PhotoId);
            Assert.True((await ReadProfileAsync()).HasPicture);
            await AssertCurrentPngPictureColorAsync(SKColors.MediumSeaGreen);

            var candidate = await _context
                .Files.AsNoTracking()
                .Where(file => file.Id > lastExistingFileId)
                .SingleAsync();
            Assert.Equal(StoredFileState.PendingDeletion, candidate.State);
            Assert.NotNull(candidate.NextAttemptAt);
            Assert.True(_storage.Contains(candidate.ObjectKey));

            var cleanupAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
            var clock = new FakeTimeProvider(new DateTimeOffset(cleanupAt, TimeSpan.Zero));
            await _context
                .Files.Where(file => file.Id == candidate.Id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(file => file.NextAttemptAt, cleanupAt)
                );
            var cleanup = new CleanupFiles(
                _context,
                _storage,
                clock,
                _scope.ServiceProvider.GetRequiredService<ILogger<CleanupFiles>>()
            );

            await cleanup.RunAsync(CancellationToken.None);

            Assert.False(_storage.Contains(candidate.ObjectKey));
            Assert.False(await _context.Files.AnyAsync(file => file.Id == candidate.Id));
            Assert.True(_storage.Contains(previousPicture.ObjectKey));
            Assert.Equal(previousPicture.Id, (await ReadCurrentStoredPictureAsync(user.Id)).Id);
            await AssertCurrentPngPictureColorAsync(SKColors.MediumSeaGreen);
        }
        finally
        {
            await _context.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS `fail_profile_picture_commit_test`"
            );
        }
    }

    [Fact]
    public async Task GetPicture_WhenStorageReadFails_ReturnsAnErrorInsteadOf404()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await PutPictureAsync(
                    ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.IndianRed),
                    "a.png",
                    "image/png"
                )
            ).StatusCode
        );

        _storage.GetFailure = new IOException("storage unavailable");
        try
        {
            var response = await _client.GetAsync("/api/profile/picture");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            _storage.GetFailure = null;
        }
    }

    [Fact]
    public async Task OverlappingUploads_TheLastCommittedPictureWins()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var previousPicture = await UploadCurrentPngAsync(user.Id, SKColors.MediumSeaGreen);

        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var putCallCount = 0;
        _storage.BeforePutAsync = async (_, _, _) =>
        {
            if (Interlocked.Increment(ref putCallCount) == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        };

        Task<HttpResponseMessage>? firstUpload = null;
        try
        {
            firstUpload = PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.Firebrick),
                "first.png",
                "image/png"
            );
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            _storage.DeleteFailure = new IOException("temporary object-store failure");
            var secondUpload = await PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.RoyalBlue),
                "second.png",
                "image/png"
            );
            Assert.Equal(HttpStatusCode.NoContent, secondUpload.StatusCode);
            var secondPicture = await ReadCurrentStoredPictureAsync(user.Id);
            Assert.True((await ReadProfileAsync()).HasPicture);
            await AssertCurrentPngPictureColorAsync(SKColors.RoyalBlue);

            releaseFirst.SetResult();
            Assert.Equal(HttpStatusCode.NoContent, (await firstUpload).StatusCode);
            Assert.True((await ReadProfileAsync()).HasPicture);
            await AssertCurrentPngPictureColorAsync(SKColors.Firebrick);

            var currentPicture = await ReadCurrentStoredPictureAsync(user.Id);

            var cleanupAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
            await _context
                .Files.Where(file => file.Id == previousPicture.Id || file.Id == secondPicture.Id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(file => file.NextAttemptAt, cleanupAt)
                );
            var clock = new FakeTimeProvider(new DateTimeOffset(cleanupAt, TimeSpan.Zero));
            var cleanup = new CleanupFiles(
                _context,
                _storage,
                clock,
                _scope.ServiceProvider.GetRequiredService<ILogger<CleanupFiles>>()
            );

            await cleanup.RunAsync(CancellationToken.None);

            _context.ChangeTracker.Clear();
            var supersededPictures = await _context
                .Files.AsNoTracking()
                .Where(file => file.Id == previousPicture.Id || file.Id == secondPicture.Id)
                .ToListAsync();
            Assert.Equal(2, supersededPictures.Count);
            Assert.All(
                supersededPictures,
                picture =>
                {
                    Assert.Equal(StoredFileState.PendingDeletion, picture.State);
                    Assert.Equal(1, picture.DeletionAttempts);
                    Assert.True(picture.NextAttemptAt > clock.GetUtcNow().UtcDateTime);
                }
            );
            Assert.True(_storage.Contains(previousPicture.ObjectKey));
            Assert.True(_storage.Contains(secondPicture.ObjectKey));

            _storage.DeleteFailure = null;
            await _context
                .Files.Where(file => file.Id == previousPicture.Id || file.Id == secondPicture.Id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(file => file.NextAttemptAt, clock.GetUtcNow().UtcDateTime)
                );
            await cleanup.RunAsync(CancellationToken.None);

            Assert.False(_storage.Contains(previousPicture.ObjectKey));
            Assert.False(_storage.Contains(secondPicture.ObjectKey));
            Assert.True(_storage.Contains(currentPicture.ObjectKey));
            Assert.DoesNotContain(currentPicture.ObjectKey, _storage.DeletedKeys);
            await AssertCurrentPngPictureColorAsync(SKColors.Firebrick);
        }
        finally
        {
            releaseFirst.TrySetResult();
            if (firstUpload is not null)
            {
                try
                {
                    await firstUpload.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception) when (firstUpload.IsFaulted || firstUpload.IsCanceled) { }
            }

            _storage.BeforePutAsync = null;
            _storage.DeleteFailure = null;
        }
    }

    private async Task<StoredPicture> ReadCurrentStoredPictureAsync(int userId) =>
        await _context
            .Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new StoredPicture(
                candidate.PhotoId!.Value,
                candidate.Photo!.ObjectKey
            ))
            .SingleAsync();

    private async Task<StoredPicture> UploadCurrentPngAsync(int userId, SKColor color)
    {
        var response = await PutPictureAsync(
            ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, color),
            "previous.png",
            "image/png"
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return await ReadCurrentStoredPictureAsync(userId);
    }

    private async Task AssertCurrentPngPictureColorAsync(SKColor expectedColor)
    {
        var response = await _client.GetAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store, no-cache, max-age=0", response.Headers.CacheControl!.ToString());
        using var decoded = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(expectedColor, decoded!.GetPixel(0, 0));
    }

    private sealed record StoredPicture(int Id, string ObjectKey);

    [Fact]
    public async Task RemovePicture_ClearsPresenceAndCanBeRetried()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await PutPictureAsync(
                    ProfilePictureTestImages.Create(SKEncodedImageFormat.Webp, SKColors.Goldenrod),
                    "a.webp",
                    "image/webp"
                )
            ).StatusCode
        );

        var removed = await _client.DeleteAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False((await ReadProfileAsync()).HasPicture);
        var updatedProfile = await _client.PutAsJsonAsync(
            "/api/profile",
            new { name = "Profile after picture removal" }
        );
        Assert.Equal(HttpStatusCode.OK, updatedProfile.StatusCode);
        Assert.False(
            (await updatedProfile.Content.ReadFromJsonAsync<ProfileResponse>())!.HasPicture
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/profile/picture")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _client.DeleteAsync("/api/profile/picture")).StatusCode
        );
    }

    [Fact]
    public async Task RemovePicture_WhenStorageDeletionFails_ClearsTheProfileAndRetriesDurably()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (
                await PutPictureAsync(
                    ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.Goldenrod),
                    "a.png",
                    "image/png"
                )
            ).StatusCode
        );

        var storedPicture = await _context
            .Users.AsNoTracking()
            .Where(candidate => candidate.Id == user.Id)
            .Select(candidate => new { Id = candidate.PhotoId!.Value, candidate.Photo!.ObjectKey })
            .SingleAsync();
        var fileId = storedPicture.Id;
        var objectKey = storedPicture.ObjectKey;
        Assert.True(_storage.Contains(objectKey));

        var removed = await _client.DeleteAsync("/api/profile/picture");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False((await ReadProfileAsync()).HasPicture);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/profile/picture")).StatusCode
        );

        var retryClockStart = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(retryClockStart);
        await _context
            .Files.Where(file => file.Id == fileId)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(
                    file => file.NextAttemptAt,
                    retryClockStart.AddMinutes(-1).UtcDateTime
                )
            );
        var cleanup = new CleanupFiles(
            _context,
            _storage,
            clock,
            _scope.ServiceProvider.GetRequiredService<ILogger<CleanupFiles>>()
        );
        _storage.DeleteFailure = new IOException("temporary object-store failure");

        try
        {
            await cleanup.RunAsync(CancellationToken.None);

            _context.ChangeTracker.Clear();
            var queuedFile = await _context
                .Files.AsNoTracking()
                .SingleAsync(file => file.Id == fileId);
            Assert.Equal(StoredFileState.PendingDeletion, queuedFile.State);
            Assert.Equal(1, queuedFile.DeletionAttempts);
            Assert.True(queuedFile.NextAttemptAt > clock.GetUtcNow().UtcDateTime);
            Assert.True(_storage.Contains(objectKey));
            Assert.False(
                await _context.Users.AnyAsync(candidate =>
                    candidate.Id == user.Id && candidate.PhotoId != null
                )
            );

            _storage.DeleteFailure = null;
            clock.Advance(TimeSpan.FromMinutes(1));
            await cleanup.RunAsync(CancellationToken.None);

            Assert.False(_storage.Contains(objectKey));
            Assert.False(await _context.Files.AnyAsync(file => file.Id == fileId));
            Assert.False((await ReadProfileAsync()).HasPicture);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await _client.GetAsync("/api/profile/picture")).StatusCode
            );
        }
        finally
        {
            _storage.DeleteFailure = null;
        }
    }

    private async Task<ProfileResponse> ReadProfileAsync()
    {
        var response = await _client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }

    private async Task<HttpResponseMessage> PutPictureAsync(
        byte[] bytes,
        string fileName,
        string submittedMediaType
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/profile/picture");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(submittedMediaType);
        form.Add(file, "File", fileName);
        request.Content = form;
        return await _client.SendAsync(request);
    }

    public void Dispose() => _scope.Dispose();
}
