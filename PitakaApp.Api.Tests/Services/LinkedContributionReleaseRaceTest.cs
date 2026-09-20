using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public sealed class LinkedContributionReleaseRaceTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task SourceDeletionWins_SplitReturnsRefreshableConcurrencyRefusalWithoutRows()
    {
        using var seedScope = factory.Services.CreateScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seed);
        var account = await AccountFactory.CreateAsync(seed, user.Id, initialBalance: 500);
        var source = await TransactionFactory.CreateAsync(seed, user.Id, account.Id, amount: 500);
        var goal = await GoalFactory.CreateAsync(seed, user.Id);
        await using var schedule = new SplitFaultSchedule();
        var split = schedule.CreateOperation();
        var paused = split.PauseAt(SplitPhase.AfterReservation);
        var splitTask = split.ExecuteAsync(
            user.Id,
            source.Id,
            Guid.NewGuid(),
            new(new(2026, 9, 19), [new(goal.Id, 100)])
        );
        await paused.WaitUntilReachedAsync();

        using (var deleteScope = factory.Services.CreateScope())
        {
            var transactions = deleteScope.ServiceProvider.GetRequiredService<TransactionService>();
            var trackedSource = await transactions.GetTrackedByIdAsync(source.Id);
            Assert.NotNull(trackedSource);
            Assert.IsType<TransactionDeleted>(await transactions.DeleteAsync(trackedSource));
        }
        paused.Release();

        var refusal = Assert.IsType<SplitRefused>(await splitTask);
        Assert.Equal("concurrent_state_changed", refusal.Reason);
        var failure = Assert.Single(refusal.Failures);
        Assert.Equal(source.Id, failure.Facts["transactionId"]);
        Assert.Equal(account.Id, failure.Facts["accountId"]);
        Assert.Equal([goal.Id], Assert.IsType<int[]>(failure.Facts["goalIds"]));
        seed.ChangeTracker.Clear();
        Assert.False(await seed.Transactions.AnyAsync(row => row.Id == source.Id));
        Assert.False(await seed.GoalContributions.AnyAsync(row => row.TransactionId == source.Id));
    }

    [Fact]
    public async Task SplitWins_WaitingSourceDeletionCannotRemoveSourceAndRetryReturnsConflict()
    {
        using var seedScope = factory.Services.CreateScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seed);
        var account = await AccountFactory.CreateAsync(seed, user.Id, initialBalance: 500);
        var source = await TransactionFactory.CreateAsync(seed, user.Id, account.Id, amount: 500);
        var goal = await GoalFactory.CreateAsync(seed, user.Id);
        await using var schedule = new SplitFaultSchedule();
        var split = schedule.CreateOperation();
        var paused = split.PauseAt(SplitPhase.BeforeCommit);
        var splitTask = split.ExecuteAsync(
            user.Id,
            source.Id,
            Guid.NewGuid(),
            new(new(2026, 9, 19), [new(goal.Id, 100)])
        );
        await paused.WaitUntilReachedAsync();

        using var deleteScope = factory.Services.CreateScope();
        var transactions = deleteScope.ServiceProvider.GetRequiredService<TransactionService>();
        var trackedSource = await transactions.GetTrackedByIdAsync(source.Id);
        Assert.NotNull(trackedSource);
        var deleteTask = transactions.DeleteAsync(trackedSource);
        await Task.Delay(250);
        Assert.False(deleteTask.IsCompleted);
        paused.Release();

        var success = Assert.IsType<SplitSucceeded>(await splitTask);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => deleteTask);
        deleteScope.Dispose();
        using var retryScope = factory.Services.CreateScope();
        var retryTransactions = retryScope.ServiceProvider.GetRequiredService<TransactionService>();
        var currentSource = await retryTransactions.GetTrackedByIdAsync(source.Id);
        Assert.NotNull(currentSource);
        var conflict = Assert.IsType<TransactionHasLinkedContributions>(
            await retryTransactions.DeleteAsync(currentSource)
        );
        Assert.Equal(source.Id, conflict.TransactionId);
        Assert.Equal(
            success.Response.Contributions[0].Id,
            Assert.Single(conflict.LinkedContributions).ContributionId
        );
        seed.ChangeTracker.Clear();
        Assert.True(await seed.Transactions.AnyAsync(row => row.Id == source.Id));
        Assert.True(await seed.GoalContributions.AnyAsync(row => row.TransactionId == source.Id));
    }
}
