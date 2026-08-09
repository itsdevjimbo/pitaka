namespace PitakaApp.Api.Tests.Actions.Auth;

using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Fixtures;

[Collection("Database collection")]
public class RegisterUserTest : IDisposable
{    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly RegisterUser _registerUser;
    private readonly PitakaDbContext _context;

    public RegisterUserTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _registerUser = _scope.ServiceProvider.GetRequiredService<RegisterUser>();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task Register_UniqueEmail_ReturnsUser()
    {
        var user = await _registerUser.ExecuteAsync(_faker.Person.FullName, _faker.Internet.Email(), "TestPass123!");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Register_NotUniqueEmail_ReturnsNull()
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

        var newUser = await _registerUser.ExecuteAsync(_faker.Person.FullName, email, "Password123");
        
        Assert.Null(newUser);
    }

    public void Dispose() => _scope.Dispose();
}