using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions.Auth;

[Collection("Database collection")]
public class LoginUserTest : IDisposable
{    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly LoginUser _loginUser;
    private readonly PitakaDbContext _context;

    public LoginUserTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _loginUser = _scope.ServiceProvider.GetRequiredService<LoginUser>();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task Login_WithCorrectValues()
    {
        var email = _faker.Internet.Email();
        var hasher = new PasswordHasher<User>();
        var password = "Test123!";
        
        _context.Users.Add(
            new User
            {
                Name = _faker.Person.FullName,
                Email = email,
                PasswordHash = hasher.HashPassword(null!, password),
            }
        );
        await _context.SaveChangesAsync();

        var loggedInUser = await _loginUser.ExecuteAsync(email, password);
        
        Assert.NotNull(loggedInUser);
    }

    [Fact]
    public async Task Login_WithWrongEmail()
    {
        var email = _faker.Internet.Email();
        var hasher = new PasswordHasher<User>();
        var password = "Test123!";

        _context.Users.Add(
            new User
            {
                Name = _faker.Person.FullName,
                Email = email,
                PasswordHash = hasher.HashPassword(null!, password),
            }
        );
        await _context.SaveChangesAsync();

        var user = await _loginUser.ExecuteAsync("wrong@email.com", password);
        
        Assert.Null(user);
    }

    [Fact]
    public async Task Login_WithWrongPassword()
    {
        var email = _faker.Internet.Email();
        var hasher = new PasswordHasher<User>();

        _context.Users.Add(
            new User
            {
                Name = _faker.Person.FullName,
                Email = email,
                PasswordHash = hasher.HashPassword(null!, hasher.HashPassword(null!, "Test123!")),
            }
        );
        await _context.SaveChangesAsync();

        var user = await _loginUser.ExecuteAsync(email, "wrongpassword123!");
        
        Assert.Null(user);
    }

    public void Dispose() => _scope.Dispose();
}