using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

// The one race redemption cannot show over HTTP — the connection pool serialises the
// requests and hides it — raced at the data layer with two DbContext instances, the
// same way RegisterUserConcurrencyTest races its check-then-insert. Two Profiles hold a
// pending change to the same address and both redeem: one wins, the loser surfaces the
// conflict rather than throwing (spec: "The one place a second seam is warranted").
[Collection("Database collection")]
public class RedeemEmailChangeConcurrencyTest : IDisposable
{
    private readonly PitakaWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public RedeemEmailChangeConcurrencyTest(PitakaWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task ConcurrentRedeem_SameNewAddress_OneWinsTheOtherReportsEmailTaken()
    {
        var raceEmail = $"race-{Guid.NewGuid():N}@example.com";
        var profileA = await UserFactory.CreateAsync(_context);
        var profileB = await UserFactory.CreateAsync(_context);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var (redeemA, inputA) = await ArrangePendingChange(scopeA, profileA.Id, raceEmail);
        var (redeemB, inputB) = await ArrangePendingChange(scopeB, profileB.Id, raceEmail);

        // Both redemptions run before either commits. The request-time check and
        // ChangeEmailAsync's own validator both saw the address free; the unique email
        // index is the backstop, and the loser lands on it — as a DbUpdateException the
        // catch translates to EmailTaken, or as the validator's DuplicateEmail once the
        // winner has committed.
        var outcomes = await Task.WhenAll(
            redeemA.ExecuteAsync(inputA),
            redeemB.ExecuteAsync(inputB));

        Assert.Single(outcomes, o => o == RedeemEmailChangeOutcome.Succeeded);
        Assert.Single(outcomes, o => o == RedeemEmailChangeOutcome.EmailTaken);

        // Exactly one Profile moved to the address — the race did not land it on both.
        var holders = await _context.Users.CountAsync(u => u.Email == raceEmail);
        Assert.Equal(1, holders);
    }

    // Put `userId` into the state a redeemable link implies — the address held as a
    // pending email with a live expiry — and mint the token the person would carry back,
    // all through the scope's own UserManager so the token's security stamp matches.
    private static async Task<(RedeemEmailChange Redeem, RedeemEmailChangeInput Input)> ArrangePendingChange(
        IServiceScope scope, int userId, string newEmail)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"Profile {userId} not found.");

        user.PendingEmail = newEmail;
        user.PendingEmailExpiresAt = DateTime.UtcNow.AddHours(1);
        var stored = await userManager.UpdateAsync(user);
        Assert.True(stored.Succeeded);

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        var redeem = new RedeemEmailChange(
            userManager,
            scope.ServiceProvider.GetRequiredService<PitakaDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>());

        return (redeem, new RedeemEmailChangeInput(userId, token));
    }

    public void Dispose() => _scope.Dispose();
}
