using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

[Collection("Database collection")]
public class GoalContributionConcurrencyTest : IDisposable
{
    private readonly PitakaWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public GoalContributionConcurrencyTest(PitakaWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task ConcurrentGoalContributionCreate_SecondOneThrowsDbUpdateConcurrencyException()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 300);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();

        var contextA = scopeA.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<PitakaDbContext>();

        var serviceA = scopeA.ServiceProvider.GetRequiredService<GoalContributionService>();
        var serviceB = scopeB.ServiceProvider.GetRequiredService<GoalContributionService>();

        var accountA = await contextA.Accounts.FirstAsync(a => a.Id == account.Id);
        var accountB = await contextB.Accounts.FirstAsync(a => a.Id == account.Id);

        var inputA = new CreateGoalContributionInput(
            null,
            200,
            DateOnly.FromDateTime(DateTime.Now)
        );
        var inputB = new CreateGoalContributionInput(
            null,
            200,
            DateOnly.FromDateTime(DateTime.Now)
        );

        await serviceA.CreateAsync(goal, accountA, inputA);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            serviceB.CreateAsync(goal, accountB, inputB) // stale Version, should throw
        );
    }

    [Fact]
    public async Task ContributionsFromDifferentAccountsToSameGoal_SecondOneIsRejected()
    {
        var user = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Checking",
            initialBalance: 300
        );
        var accountB = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Savings",
            initialBalance: 300
        );
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var serviceA = scopeA.ServiceProvider.GetRequiredService<GoalContributionService>();
        var serviceB = scopeB.ServiceProvider.GetRequiredService<GoalContributionService>();

        var guardedAccountA = await contextA.Accounts.SingleAsync(a => a.Id == accountA.Id);
        var guardedAccountB = await contextB.Accounts.SingleAsync(a => a.Id == accountB.Id);
        var guardedGoalA = await contextA.Goals.SingleAsync(g => g.Id == goal.Id);
        var guardedGoalB = await contextB.Goals.SingleAsync(g => g.Id == goal.Id);
        var input = new CreateGoalContributionInput(null, 100, DateOnly.FromDateTime(DateTime.Now));

        await serviceA.CreateAsync(guardedGoalA, guardedAccountA, input);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            serviceB.CreateAsync(guardedGoalB, guardedAccountB, input)
        );
    }

    public void Dispose() => _scope.Dispose();
}
