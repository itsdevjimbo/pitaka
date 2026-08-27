using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

// Uses RealAuthWebApplicationFactory instead of PitakaWebApplicationFactory — the only
// difference that matters here is TestAuthHandler is NOT registered, so [Authorize]
// falls through to the real JwtBearerHandler. These are the only tests in the suite
// that actually exercise token signature/issuer/audience/expiry validation.
[Collection("RealAuthDatabase collection")]
public class AuthControllerRealAuthTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public AuthControllerRealAuthTest(RealAuthWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Me_WithRealJwtFromLogin_ReturnsOk()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = UserFactory.DefaultPassword,
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task Me_WithTamperedToken_ReturnsUnauthorized()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = UserFactory.DefaultPassword,
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Flip the last character of the signature segment — same claims, invalid signature.
        var tamperedToken = loginBody!.Token[..^1] + (loginBody.Token[^1] == 'A' ? 'B' : 'A');

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithNoToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}
