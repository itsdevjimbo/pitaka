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

        var result = await _loginUser.ExecuteAsync(new LoginInput(email, password));

        Assert.Equal(LoginOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.User);
    }

    [Fact]
    public async Task Login_WithWrongEmail()
    {
        var email = _faker.Internet.Email();
        const string password = "Test123!";
        await UserFactory.CreateAsync(_context, email, password);

        var result = await _loginUser.ExecuteAsync(new LoginInput("wrong@email.com", password));

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task Login_WithWrongPassword()
    {
        var email = _faker.Internet.Email();
        await UserFactory.CreateAsync(_context, email, "Test123!");

        var result = await _loginUser.ExecuteAsync(new LoginInput(email, "wrongpassword123!"));

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task Login_WithUnconfirmedEmail_ReturnsNotConfirmed()
    {
        var email = _faker.Internet.Email();
        const string password = "Test123!";
        var user = await UserFactory.CreateAsync(_context, email, password);
        user.EmailConfirmed = false;
        await _context.SaveChangesAsync();

        var result = await _loginUser.ExecuteAsync(new LoginInput(email, password));

        Assert.Equal(LoginOutcome.NotConfirmed, result.Outcome);
        Assert.Null(result.User);
    }

    // Pins the PreSignInCheck ordering (confirmed-account gate before password check):
    // an unconfirmed Profile gets NotConfirmed even with the wrong password, not
    // InvalidCredentials. See ADR 0012 and #116.
    [Fact]
    public async Task Login_WithUnconfirmedEmail_AndWrongPassword_StillReturnsNotConfirmed()
    {
        var email = _faker.Internet.Email();
        var user = await UserFactory.CreateAsync(_context, email, "Test123!");
        user.EmailConfirmed = false;
        await _context.SaveChangesAsync();

        var result = await _loginUser.ExecuteAsync(new LoginInput(email, "wrongpassword123!"));

        Assert.Equal(LoginOutcome.NotConfirmed, result.Outcome);
        Assert.Null(result.User);
    }

    public void Dispose() => _scope.Dispose();
}
