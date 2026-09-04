using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions.Auth;

[Collection("Database collection")]
public class ResetPasswordTest : IDisposable
{
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public ResetPasswordTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    // The lifetime boundary, pinned from the far side: a token left past its ExpiresAt is
    // rejected, and the password is untouched. FakeTimeProvider advances the clock without
    // sleeping and without freezing time for every other test on the shared host.
    [Fact]
    public async Task Reset_WithExpiredToken_IsRejected_AndPasswordUnchanged()
    {
        var mintedAt = new DateTime(2026, 08, 29, 12, 0, 0, DateTimeKind.Utc);
        var lifetime = TimeSpan.FromHours(1);
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(mintedAt);

        var (user, plaintext, originalHash) = await SeedTokenAsync(expiresAt: mintedAt + lifetime);

        clock.SetUtcNow(mintedAt + lifetime); // exactly at expiry — already too late
        var action = new ResetPassword(_context, clock);

        var succeeded = await action.ExecuteAsync(new ResetPasswordInput(plaintext, "brand-new-password"));

        Assert.False(succeeded);
        Assert.Equal(originalHash, user.PasswordHash);

        var storedToken = await _context.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.Null(storedToken.UsedAt);
    }

    // The near side of the same boundary: one tick before ExpiresAt the token still works,
    // spends itself, and changes the hash. Pinned from both sides so an off-by-one in the
    // comparison cannot pass.
    [Fact]
    public async Task Reset_OneTickBeforeExpiry_Succeeds()
    {
        var mintedAt = new DateTime(2026, 08, 29, 12, 0, 0, DateTimeKind.Utc);
        var expiresAt = mintedAt + TimeSpan.FromHours(1);
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(mintedAt);

        var (user, plaintext, originalHash) = await SeedTokenAsync(expiresAt);

        clock.SetUtcNow(expiresAt - TimeSpan.FromTicks(1));
        var action = new ResetPassword(_context, clock);

        var succeeded = await action.ExecuteAsync(new ResetPasswordInput(plaintext, "brand-new-password"));

        Assert.True(succeeded);
        Assert.NotEqual(originalHash, user.PasswordHash);

        var storedToken = await _context.PasswordResetTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.NotNull(storedToken.UsedAt);
    }

    private async Task<(User user, string plaintext, string? originalHash)> SeedTokenAsync(DateTime expiresAt)
    {
        var user = await UserFactory.CreateAsync(_context, _faker.Internet.Email());

        var plaintext = Guid.NewGuid().ToString("N");
        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = PasswordResetToken.Hash(plaintext),
            ExpiresAt = expiresAt,
        });
        await _context.SaveChangesAsync();

        return (user, plaintext, user.PasswordHash);
    }

    public void Dispose() => _scope.Dispose();
}
