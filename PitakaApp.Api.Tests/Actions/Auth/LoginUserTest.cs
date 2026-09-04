using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Tests.Factories;
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
        const string password = "Test123!";
        await UserFactory.CreateAsync(_context, email, password);

        var loggedInUser = await _loginUser.ExecuteAsync(new LoginInput(email, password));

        Assert.NotNull(loggedInUser);
    }

    [Fact]
    public async Task Login_WithWrongEmail()
    {
        var email = _faker.Internet.Email();
        const string password = "Test123!";
        await UserFactory.CreateAsync(_context, email, password);

        var user = await _loginUser.ExecuteAsync(new LoginInput("wrong@email.com", password));

        Assert.Null(user);
    }

    [Fact]
    public async Task Login_WithWrongPassword()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email, "Test123!");

        var user = await _loginUser.ExecuteAsync(new LoginInput(email, "wrongpassword123!"));

        Assert.Null(user);
    }

    public void Dispose() => _scope.Dispose();
}
