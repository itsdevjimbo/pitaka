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

[Collection("Database collection")]
public class AuthControllerTest : IDisposable
{
    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public AuthControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();   
    }

    [Fact]
    public async Task Login_WithExistingUser_ReturnsOkWithUser()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var request = new { email, password = UserFactory.DefaultPassword };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(body);
        Assert.NotNull(body.Token);
        Assert.Equal(request.email, body!.User.Email);
    }

    [Fact]
    public async Task Login_WithInvalidCredential_ReturnsUnauthorized()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var wrongEmailRequest = new
        {
            email = "wrong@email.com",
            password = UserFactory.DefaultPassword,
        };

        var response = await _client.PostAsJsonAsync("/api/auth/login", wrongEmailRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var wrongPasswordRequest = new
        {
            email,
            password = "WrongPassword123!"
        };

        response = await _client.PostAsJsonAsync("/api/auth/login", wrongPasswordRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithNewEmail_ReturnsOkWithUser()
    {
        var request = new
        {
            name = _faker.Person.FullName,
            email = _faker.Internet.Email(),
            password = "TestPass123!",
        };

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal(request.email, body!.Email);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ReturnsConflict()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email);

        var request = new
        {
            Name = _faker.Person.FullName,
            Email = email,
            Password = "SomePassword123!",
        };

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal("A user with this email already exists.", problem!.Detail);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var email = _faker.Internet.Email();

        var user = await UserFactory.CreateAsync(_context, email);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(email, body!.Email);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}