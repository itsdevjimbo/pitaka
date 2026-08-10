namespace PitakaApp.Api.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Fixtures;

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
        var password = "TestPass123!";
        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Name = _faker.Person.FullName,
            Email = email,
            PasswordHash = hasher.HashPassword(null!, password),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new { email, password };

        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.NotNull(body);
        Assert.Equal(request.email, body!.Email);
    }

    [Fact]
    public async Task Login_WithInvalidCredential_ReturnsUnauthorized()
    {
        var email = _faker.Internet.Email();
        var password = "TestPass123!";
        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Name = _faker.Person.FullName,
            Email = email,
            PasswordHash = hasher.HashPassword(null!, password),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var wrongEmailRequest = new 
        { 
            email = "wrong@email.com", 
            password 
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

        var user = new User
        {
            Name = _faker.Person.FullName,
            Email = email,
            PasswordHash = "NotHashPasswordAndItsOKay",
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();


        var request = new
        {
            Name = _faker.Person.FullName,
            Email = email,
            Password = "NotHashPasswordAndItsOKay",
        };

        var response = await _client.PostAsJsonAsync("/api/Auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var errorMessage = await response.Content.ReadAsStringAsync();

        Assert.Equal("A user with this email already exists.", errorMessage);
    }
    

    public void Dispose() => _scope.Dispose();
}