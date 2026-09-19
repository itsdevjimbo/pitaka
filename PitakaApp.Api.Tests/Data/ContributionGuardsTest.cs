using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Data;

[Collection("Database collection")]
public class ContributionGuardsTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task Capture_ReturnsEligibilityCapacityHeadroomAndOverrunObservations()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 1000);
        var goal = await GoalFactory.CreateAsync(context, user.Id, targetAmount: 500);
        var otherGoal = await GoalFactory.CreateAsync(context, user.Id, name: "Other goal");
        var transaction = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            TransactionType.Income,
            amount: 600
        );

        await GoalContributionFactory.CreateAsync(
            context,
            goal.Id,
            account.Id,
            transaction.Id,
            amount: 250
        );
        await GoalContributionFactory.CreateAsync(context, goal.Id, account.Id, amount: 200);
        await GoalContributionFactory.CreateAsync(context, otherGoal.Id, account.Id, amount: 100);
        context.ChangeTracker.Clear();

        var guards = new ContributionGuards(context);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id], transaction.Id);

        Assert.True(snapshot.Income!.IsEligible);
        Assert.Equal(250m, snapshot.TransactionCapacity!.LinkedTotal);
        Assert.Equal(350m, snapshot.TransactionCapacity.RemainingCapacity);
        Assert.Equal(550m, snapshot.AccountHeadroom!.EarmarkedTotal);
        Assert.Equal(450m, snapshot.AccountHeadroom.AvailableHeadroom);

        var goalObservation = Assert.Single(snapshot.Goals);
        Assert.True(goalObservation.IsEligibleForLinkedContribution);
        var overrun = goalObservation.ObserveOverrun(100);
        Assert.Equal(450m, overrun.CurrentAmount);
        Assert.Equal(500m, overrun.TargetAmount);
        Assert.Equal(550m, overrun.ProposedAmount);
        Assert.True(overrun.ExceedsTarget);
    }

    [Fact]
    public async Task ContributionRelease_DoesNotInvalidateCapturedAccountOrGoalGuards()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);
        var released = await GoalContributionFactory.CreateAsync(
            seedContext,
            goal.Id,
            account.Id,
            amount: 200
        );

        using var createScope = factory.Services.CreateScope();
        using var releaseScope = factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var releaseContext = releaseScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(createContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        var releasedInOtherContext = await releaseContext.GoalContributions.SingleAsync(gc =>
            gc.Id == released.Id
        );
        releaseContext.GoalContributions.Remove(releasedInOtherContext);
        await releaseContext.SaveChangesAsync();

        createContext.GoalContributions.Add(
            new GoalContribution
            {
                GoalId = goal.Id,
                AccountId = account.Id,
                Amount = 100,
                ContributionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        guards.MarkConcurrencyGuardsModified(snapshot);
        await createContext.SaveChangesAsync();
    }

    [Fact]
    public async Task DifferentGoalCascadeRelease_DoesNotInvalidateCapturedAccountGuard()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var releasedGoal = await GoalFactory.CreateAsync(seedContext, user.Id, "Released goal");
        var destinationGoal = await GoalFactory.CreateAsync(
            seedContext,
            user.Id,
            "Destination goal"
        );
        await GoalContributionFactory.CreateAsync(
            seedContext,
            releasedGoal.Id,
            account.Id,
            amount: 200
        );

        using var createScope = factory.Services.CreateScope();
        using var releaseScope = factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var releaseContext = releaseScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(createContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [destinationGoal.Id]);

        var releasedGoalInOtherContext = await releaseContext.Goals.SingleAsync(g =>
            g.Id == releasedGoal.Id
        );
        releaseContext.Goals.Remove(releasedGoalInOtherContext);
        await releaseContext.SaveChangesAsync();

        createContext.GoalContributions.Add(
            new GoalContribution
            {
                GoalId = destinationGoal.Id,
                AccountId = account.Id,
                Amount = 100,
                ContributionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        guards.MarkConcurrencyGuardsModified(snapshot);
        await createContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GoalDeletionAfterCapture_RejectsCreationAndLeavesNoContribution()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);

        using var createScope = factory.Services.CreateScope();
        using var deleteScope = factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var deleteContext = deleteScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(createContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        deleteContext.Goals.Remove(await deleteContext.Goals.SingleAsync(g => g.Id == goal.Id));
        await deleteContext.SaveChangesAsync();

        createContext.GoalContributions.Add(
            new GoalContribution
            {
                GoalId = goal.Id,
                AccountId = account.Id,
                Amount = 100,
                ContributionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
        guards.MarkConcurrencyGuardsModified(snapshot);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => createContext.SaveChangesAsync());

        using var assertScope = factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.False(await assertContext.GoalContributions.AnyAsync(gc => gc.GoalId == goal.Id));
    }

    [Fact]
    public async Task AccountRetirementAfterCapture_RejectsCreation()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);

        using var createScope = factory.Services.CreateScope();
        using var retireScope = factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var retireContext = retireScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(createContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        var retiredAccount = await retireContext.Accounts.SingleAsync(a => a.Id == account.Id);
        retiredAccount.Deactivate();
        await retireContext.SaveChangesAsync();

        AddContribution(createContext, goal.Id, account.Id);
        guards.MarkConcurrencyGuardsModified(snapshot);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            createContext.SaveChangesAsync()
        );
    }

    [Fact]
    public async Task GoalLifecycleChangeAfterCapture_RejectsCreation()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);

        using var createScope = factory.Services.CreateScope();
        using var lifecycleScope = factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var lifecycleContext = lifecycleScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(createContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        var changedGoal = await lifecycleContext.Goals.SingleAsync(g => g.Id == goal.Id);
        changedGoal.Status = GoalStatus.Abandoned;
        await lifecycleContext.SaveChangesAsync();

        AddContribution(createContext, goal.Id, account.Id);
        guards.MarkConcurrencyGuardsModified(snapshot);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            createContext.SaveChangesAsync()
        );
    }

    [Fact]
    public async Task RecordedTransactionCreationAfterCapture_RejectsContributionCreation()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);

        using var contributionScope = factory.Services.CreateScope();
        using var transactionScope = factory.Services.CreateScope();
        var contributionContext =
            contributionScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var transactionContext =
            transactionScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(contributionContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        var transactionService =
            transactionScope.ServiceProvider.GetRequiredService<TransactionService>();
        var transactionAccount = await transactionContext.Accounts.SingleAsync(a =>
            a.Id == account.Id
        );
        await transactionService.CreateAsync(
            transactionAccount,
            new CreateTransactionInput(TransactionType.Expense, 50)
        );

        AddContribution(contributionContext, goal.Id, account.Id);
        guards.MarkConcurrencyGuardsModified(snapshot);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            contributionContext.SaveChangesAsync()
        );
    }

    [Fact]
    public async Task RecordedTransactionRemovalAfterCapture_RejectsContributionCreation()
    {
        using var seedScope = factory.Services.CreateScope();
        var seedContext = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seedContext);
        var account = await AccountFactory.CreateAsync(seedContext, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(seedContext, user.Id);
        var transaction = await TransactionFactory.CreateAsync(
            seedContext,
            user.Id,
            account.Id,
            TransactionType.Expense,
            50
        );

        using var contributionScope = factory.Services.CreateScope();
        using var transactionScope = factory.Services.CreateScope();
        var contributionContext =
            contributionScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var transactionContext =
            transactionScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var guards = new ContributionGuards(contributionContext);
        var snapshot = await guards.CaptureAsync(user.Id, account.Id, [goal.Id]);

        var transactionService =
            transactionScope.ServiceProvider.GetRequiredService<TransactionService>();
        var transactionToRemove = await transactionContext.Transactions.SingleAsync(t =>
            t.Id == transaction.Id
        );
        await transactionService.DeleteAsync(transactionToRemove);

        AddContribution(contributionContext, goal.Id, account.Id);
        guards.MarkConcurrencyGuardsModified(snapshot);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            contributionContext.SaveChangesAsync()
        );
    }

    private static void AddContribution(PitakaDbContext context, int goalId, int accountId)
    {
        context.GoalContributions.Add(
            new GoalContribution
            {
                GoalId = goalId,
                AccountId = accountId,
                Amount = 100,
                ContributionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            }
        );
    }
}
