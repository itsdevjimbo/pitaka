using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

[Collection("Database collection")]
public class AccountConcurrencyTest : IDisposable
{
    private readonly PitakaWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public AccountConcurrencyTest(PitakaWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task ConcurrentSaves_SecondOneThrowsDbUpdateConcurrencyException()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<PitakaDbContext>();

        // Both "requests" read the account before either one saves — same Version.
        var accountA = await contextA.Accounts.FirstAsync(a => a.Id == account.Id);
        var accountB = await contextB.Accounts.FirstAsync(a => a.Id == account.Id);

        accountA.Increase(100);
        await contextA.SaveChangesAsync(); // wins the race, Version increments

        accountB.Increase(50);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            contextB.SaveChangesAsync() // stale Version, should throw
        );
    }

    [Fact]
    public async Task AccountDeletionCannotCascadeAContributionCommittedAfterEligibilityChecks()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        using var deleteScope = _factory.Services.CreateScope();
        using var createScope = _factory.Services.CreateScope();
        var deleteContext = deleteScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var accountToDelete = await deleteContext.Accounts.SingleAsync(a => a.Id == account.Id);
        Assert.False(
            await deleteContext.GoalContributions.AnyAsync(gc => gc.AccountId == account.Id)
        );

        var createService =
            createScope.ServiceProvider.GetRequiredService<GoalContributionService>();
        var guardedAccount = await createContext.Accounts.SingleAsync(a => a.Id == account.Id);
        var guardedGoal = await createContext.Goals.SingleAsync(g => g.Id == goal.Id);
        await createService.CreateAsync(
            guardedGoal,
            guardedAccount,
            new CreateGoalContributionInput(null, 100, DateOnly.FromDateTime(DateTime.UtcNow))
        );

        deleteContext.Accounts.Remove(accountToDelete);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            deleteContext.SaveChangesAsync()
        );

        using var assertScope = _factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.True(await assertContext.Accounts.AnyAsync(a => a.Id == account.Id));
        Assert.True(
            await assertContext.GoalContributions.AnyAsync(gc => gc.AccountId == account.Id)
        );
    }

    [Fact]
    public async Task TransferWithStaleDestinationVersion_RollsBackSourceAndTransaction()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Source",
            initialBalance: 500
        );
        var destination = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Destination",
            initialBalance: 500
        );

        using var transferScope = _factory.Services.CreateScope();
        using var competingScope = _factory.Services.CreateScope();
        var transferContext = transferScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var competingContext = competingScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var transactionService =
            transferScope.ServiceProvider.GetRequiredService<TransactionService>();
        var guardedSource = await transferContext.Accounts.SingleAsync(a => a.Id == source.Id);
        await transferContext.Accounts.SingleAsync(a => a.Id == destination.Id);

        var competingDestination = await competingContext.Accounts.SingleAsync(a =>
            a.Id == destination.Id
        );
        competingDestination.Increase(25);
        await competingContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            transactionService.CreateAsync(
                guardedSource,
                new CreateTransactionInput(
                    TransactionType.Transfer,
                    100,
                    TransferToAccountId: destination.Id
                )
            )
        );

        using var assertScope = _factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.Equal(
            500m,
            (await assertContext.Accounts.SingleAsync(a => a.Id == source.Id)).CurrentBalance
        );
        Assert.Equal(
            525m,
            (await assertContext.Accounts.SingleAsync(a => a.Id == destination.Id)).CurrentBalance
        );
        Assert.False(
            await assertContext.Transactions.AnyAsync(t =>
                t.AccountId == source.Id && t.TransferToAccountId == destination.Id
            )
        );
    }

    public void Dispose() => _scope.Dispose();
}
