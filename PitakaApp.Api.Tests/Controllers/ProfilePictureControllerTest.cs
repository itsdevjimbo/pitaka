using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task UploadPicture_WhenStorageWriteFails_LeavesThePreviousPictureCurrent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var initialUpload = await PutPictureAsync(
            ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.MediumSeaGreen),
            "old.png",
            "image/png"
        );
        Assert.Equal(HttpStatusCode.NoContent, initialUpload.StatusCode);
        var previous = await _context
            .Users.AsNoTracking()
            .Where(candidate => candidate.Id == user.Id)
            .Select(candidate => candidate.PhotoId)
            .SingleAsync();
        Assert.NotNull(previous);
        var previousObjectKey = await _context
            .Files.AsNoTracking()
            .Where(file => file.Id == previous)
            .Select(file => file.ObjectKey)
            .SingleAsync();

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
            Assert.Equal(previous, afterFailure.PhotoId);
            Assert.True(afterFailure.HasPicture);

            var read = await _client.GetAsync("/api/profile/picture");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(
                _storage.Read(previousObjectKey),
                await read.Content.ReadAsByteArrayAsync()
            );
        }
        finally
        {
            _storage.PutFailure = null;
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

        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var call = 0;
        _storage.BeforePutAsync = async (_, _, _) =>
        {
            if (Interlocked.Increment(ref call) == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        };

        try
        {
            var firstUpload = PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.Firebrick),
                "first.png",
                "image/png"
            );
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var secondUpload = await PutPictureAsync(
                ProfilePictureTestImages.Create(SKEncodedImageFormat.Png, SKColors.RoyalBlue),
                "second.png",
                "image/png"
            );
            Assert.Equal(HttpStatusCode.NoContent, secondUpload.StatusCode);

            releaseFirst.SetResult();
            Assert.Equal(HttpStatusCode.NoContent, (await firstUpload).StatusCode);

            var current = await _context
                .Users.AsNoTracking()
                .Where(candidate => candidate.Id == user.Id)
                .Select(candidate => candidate.Photo!.ObjectKey)
                .SingleAsync();
            Assert.NotNull(current);
            using var decoded = SKBitmap.Decode(_storage.Read(current!));
            Assert.Equal(SKColors.Firebrick, decoded!.GetPixel(0, 0));
        }
        finally
        {
            releaseFirst.TrySetResult();
            _storage.BeforePutAsync = null;
        }
    }

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
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/profile/picture")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _client.DeleteAsync("/api/profile/picture")).StatusCode
        );
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
