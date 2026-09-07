using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// GET and PUT api/profile under the stubbed TestAuthHandler. The real JWT path is
// ProfileControllerRealAuthTest. The GET cases are the GET me tests moved verbatim to
// the new route (spec ticket 07) — same behaviour, new address; the PUT cases are the
// name write (spec ticket 08, stories 6–14).
[Collection("Database collection")]
public class ProfileControllerTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public ProfileControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProfile_WithValidToken_ReturnsCurrentProfile()
    {
        var email = _faker.Internet.Email();

        var user = await UserFactory.CreateAsync(_context, email);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task GetProfile_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Profile self-service ticket 08: PUT api/profile, the name write ────────────

    [Fact]
    public async Task UpdateProfile_WithValidName_ReturnsTheUpdatedProfile()
    {
        var email = _faker.Internet.Email();
        var user = await UserFactory.CreateAsync(_context, email);
        _client.ActAsUser(user);

        var newName = _faker.Person.FullName;

        // The body carries only the name — there is no field for a current password.
        var response = await _client.PutAsJsonAsync("/api/profile", new { name = newName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(newName, body!.Name);
        Assert.Equal(email, body.Email);
        Assert.Null(body.PendingEmail);
    }

    [Fact]
    public async Task UpdateProfile_LeavesTheEmailAndAnyPendingEmailUntouched()
    {
        var email = _faker.Internet.Email();
        var pendingEmail = _faker.Internet.Email();
        var user = await UserFactory.CreateAsync(_context, email);
        user.PendingEmail = pendingEmail;
        user.PendingEmailExpiresAt = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();
        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/profile", new { name = _faker.Person.FullName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(email, body!.Email);
        Assert.Equal(pendingEmail, body.PendingEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateProfile_WithEmptyOrMissingName_IsRejectedAsValidationProblemNamingName(string? name)
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/profile", new { name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Name", problem!.Errors.Keys);
    }

    [Fact]
    public async Task UpdateProfile_WithNameLongerThanRegistrationAllows_IsRejectedAsValidationProblemNamingName()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.PutAsJsonAsync("/api/profile", new { name = new string('a', 256) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Name", problem!.Errors.Keys);
    }

    [Fact]
    public async Task UpdateProfile_WithTheLongestNameRegistrationAllows_IsAccepted()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var maxLengthName = new string('a', 255);

        var response = await _client.PutAsJsonAsync("/api/profile", new { name = maxLengthName });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(maxLengthName, body!.Name);
    }

    [Fact]
    public async Task UpdateProfile_PersistsTheName_ASubsequentReadReturnsIt()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var newName = _faker.Person.FullName;
        await _client.PutAsJsonAsync("/api/profile", new { name = newName });

        var reread = await _client.GetAsync("/api/profile");
        var body = await reread.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.Equal(newName, body!.Name);
    }

    [Fact]
    public async Task UpdateProfile_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PutAsJsonAsync("/api/profile", new { name = _faker.Person.FullName });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}
