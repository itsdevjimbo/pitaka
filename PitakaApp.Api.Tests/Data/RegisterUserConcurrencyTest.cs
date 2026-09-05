using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

// The check-then-insert race in RegisterUser, tested at the action seam the same way
// AccountConcurrencyTest tests its analogue — two contexts from two scopes, not two real
// concurrent HTTP requests (the connection pool serialises those anyway).
[Collection("Database collection")]
public class RegisterUserConcurrencyTest : IDisposable
{
    private readonly PitakaWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public RegisterUserConcurrencyTest(PitakaWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task ConcurrentRegister_SameEmail_OneWinsTheOtherReturnsNullWithoutThrowing()
    {
        var email = $"race-{Guid.NewGuid():N}@example.com";

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var userManagerA = scopeA.ServiceProvider.GetRequiredService<UserManager<User>>();
        var userManagerB = scopeB.ServiceProvider.GetRequiredService<UserManager<User>>();

        var registerA = new RegisterUser(userManagerA, scopeA.ServiceProvider.GetRequiredService<SendEmailConfirmation>());
        var registerB = new RegisterUser(userManagerB, scopeB.ServiceProvider.GetRequiredService<SendEmailConfirmation>());

        var inputA = new RegisterInput("Racer A", email, "TestPass123!");
        var inputB = new RegisterInput("Racer B", email, "TestPass123!");

        // Both run before either commits: their AnyAsync pre-checks both see no row, so
        // both reach the insert and the loser lands on the unique-index write failure the
        // catch translates back to EmailTaken.
        var results = await Task.WhenAll(
            registerA.ExecuteAsync(inputA),
            registerB.ExecuteAsync(inputB)
        );

        Assert.Single(results, r => r.Outcome == RegisterOutcome.Succeeded);
        Assert.Single(results, r => r.Outcome == RegisterOutcome.EmailTaken);

        // One Profile, not two — the race did not double-insert.
        var rows = await _context.Users.CountAsync(u => u.Email == email);
        Assert.Equal(1, rows);
    }

    public void Dispose() => _scope.Dispose();
}
