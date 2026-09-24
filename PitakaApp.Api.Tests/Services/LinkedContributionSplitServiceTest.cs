using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Jobs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public class LinkedContributionSplitServiceTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task Split_CreatesOrderedRowsAndReplaysOriginalSnapshot()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
        var transaction = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 300
        );
        var first = await GoalFactory.CreateAsync(context, user.Id, name: "First");
        var second = await GoalFactory.CreateAsync(context, user.Id, name: "Second");
        var service = scope.ServiceProvider.GetRequiredService<LinkedContributionSplitService>();
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(
            new DateOnly(2026, 9, 19),
            [new(second.Id, 100, " "), new(first.Id, 50)]
        );

        var success = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(user.Id, transaction.Id, key, input)
        );
        Assert.Equal(201, success.StatusCode);
        Assert.Equal(150, success.Response.LinkedTotal);
        Assert.Equal(150, success.Response.RemainingCapacity);
        Assert.Equal(350, success.Response.Account.AvailableHeadroom);
        Assert.Equal(500, success.Response.Account.CurrentBalance);
        Assert.Equal([second.Id, first.Id], success.Response.Contributions.Select(c => c.GoalId));
        Assert.All(
            success.Response.Contributions,
            c => Assert.Equal(input.ContributionDate, c.ContributionDate)
        );
        var replay = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(user.Id, transaction.Id, key, input)
        );
        Assert.Equal(
            success.Response.Contributions.Select(c => c.Id),
            replay.Response.Contributions.Select(c => c.Id)
        );
    }

    [Fact]
    public async Task Refusal_CollectsCapacityAndRowFailuresWithoutReservingKey()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 50);
        var source = await TransactionFactory.CreateAsync(context, user.Id, account.Id, amount: 60);
        var goal = await GoalFactory.CreateAsync(
            context,
            user.Id,
            targetAmount: 10,
            status: PitakaApp.Api.Enums.GoalStatus.Abandoned
        );
        var service = scope.ServiceProvider.GetRequiredService<LinkedContributionSplitService>();
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        var refusal = Assert.IsType<SplitRefused>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal("split_rejected", refusal.Reason);
        Assert.Equal(
            [
                "account_headroom_exceeded",
                "goal_inactive",
                "target_overrun_acknowledgement_required",
                "transaction_capacity_exceeded",
            ],
            refusal.Failures.Select(f => f.Reason).Order()
        );
        Assert.True(refusal.Created == false);
        goal.Status = PitakaApp.Api.Enums.GoalStatus.Active;
        await context.SaveChangesAsync();
        var success = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(
                user.Id,
                source.Id,
                key,
                input with
                {
                    Contributions = [new(goal.Id, 20, AcknowledgeTargetOverrun: true)],
                }
            )
        );
        Assert.Equal(20, success.Response.LinkedTotal);
    }

    [Fact]
    public async Task KeyIdentity_NormalizesDecimalsButPreservesOrderDateNoteAndAcknowledgement()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var second = await GoalFactory.CreateAsync(context, user.Id, name: "Other");
        var service = scope.ServiceProvider.GetRequiredService<LinkedContributionSplitService>();
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(
            new(2026, 9, 19),
            [new(goal.Id, 10), new(second.Id, 20)]
        );
        var success = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        var replay = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(
                user.Id,
                source.Id,
                Guid.Parse(key.ToString().ToUpperInvariant()),
                input with
                {
                    Contributions = [new(goal.Id, 10.00m, null, false), new(second.Id, 20.0m)],
                }
            )
        );
        Assert.Equal(success.Response.Contributions[0].Id, replay.Response.Contributions[0].Id);
        foreach (
            var changed in new[]
            {
                input with
                {
                    ContributionDate = input.ContributionDate.AddDays(1),
                },
                input with
                {
                    Contributions = [.. input.Contributions.Reverse()],
                },
                input with
                {
                    Contributions = [new(goal.Id, 11), new(second.Id, 20)],
                },
                input with
                {
                    Contributions = [new(goal.Id, 10, ""), new(second.Id, 20)],
                },
                input with
                {
                    Contributions = [new(goal.Id, 10, " "), new(second.Id, 20)],
                },
                input with
                {
                    Contributions =
                    [
                        new(goal.Id, 10, AcknowledgeTargetOverrun: true),
                        new(second.Id, 20),
                    ],
                },
            }
        )
        {
            Assert.IsType<SplitIdempotencyMismatch>(
                await service.ExecuteAsync(user.Id, source.Id, key, changed)
            );
        }

        Assert.IsType<SplitIdempotencyMismatch>(
            await service.ExecuteAsync(user.Id, source.Id + 10000, key, input)
        );
    }

    [Fact]
    public async Task InvalidRows_AreRejectedBeforeResourceChecksAndReportEveryDuplicateIndex()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<LinkedContributionSplitService>();
        var invalid = Assert.IsType<SplitInvalid>(
            await service.ExecuteAsync(
                1,
                int.MaxValue,
                Guid.NewGuid(),
                new(new(2026, 9, 19), [new(99, 0), new(99, 0.001m), new(88, -1)])
            )
        );
        Assert.Equal(
            [
                "contributions[0].amount",
                "contributions[0].goalId",
                "contributions[1].amount",
                "contributions[1].goalId",
                "contributions[2].amount",
            ],
            invalid.Errors.Keys.Order()
        );
        Assert.IsType<SplitInvalid>(
            await service.ExecuteAsync(1, int.MaxValue, Guid.NewGuid(), new(new(2026, 9, 19), []))
        );
    }

    [Theory]
    [InlineData("same")]
    [InlineData("different")]
    [InlineData("rollback")]
    public async Task SameKey_ConcurrentRequestsRecoverOneCommittedResult(string scenario)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        await using var schedule = new SplitFaultSchedule();
        var winner = schedule.CreateOperation();
        var reserved = winner.PauseAt(
            SplitPhase.AfterReservation,
            scenario == "rollback"
                ? new InvalidOperationException("Injected winner rollback")
                : null
        );
        var contender = schedule.CreateOperation();
        var contenderStarted = contender.PauseAt(SplitPhase.BeforeReservation);
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        var first = winner.ExecuteAsync(user.Id, source.Id, key, input);
        await reserved.WaitUntilReachedAsync();
        var second = contender.ExecuteAsync(
            user.Id,
            source.Id,
            key,
            scenario == "different" ? input with { Contributions = [new(goal.Id, 50)] } : input
        );
        await contenderStarted.WaitUntilReachedAsync();
        contenderStarted.Release();
        reserved.Release();
        if (scenario == "rollback")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
            Assert.Equal(100, Assert.IsType<SplitSucceeded>(await second).Response.LinkedTotal);
            return;
        }
        var success = Assert.IsType<SplitSucceeded>(await first);
        if (scenario == "different")
        {
            Assert.IsType<SplitIdempotencyMismatch>(await second);
            return;
        }
        var replay = Assert.IsType<SplitSucceeded>(await second);
        Assert.Equal(success.Response.Contributions[0].Id, replay.Response.Contributions[0].Id);
        Assert.Equal(100, replay.Response.LinkedTotal);
    }

    [Fact]
    public async Task SameKey_ContentionIsBoundedAndRecoveryKeepsOriginalKey()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        await using var schedule = new SplitFaultSchedule();
        var winner = schedule.CreateOperation();
        var reserved = winner.PauseAt(SplitPhase.AfterReservation);
        var contender = schedule.CreateOperation();
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        var first = winner.ExecuteAsync(user.Id, source.Id, key, input);
        await reserved.WaitUntilReachedAsync();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var outcome = await contender
                .ExecuteAsync(user.Id, source.Id, key, input)
                .WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsType<SplitOutcomeUnknown>(outcome);
            Assert.InRange(clock.Elapsed.TotalSeconds, 4, 7);
        }
        finally
        {
            reserved.Release();
        }
        var success = Assert.IsType<SplitSucceeded>(await first);
        var replay = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(success.Response.Contributions[0].Id, replay.Response.Contributions[0].Id);
    }

    [Fact]
    public async Task CompetingKey_AfterObservationsReturnsResourceOnlyConcurrencyRefusal()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 150);
        var source = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 150
        );
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.On(
            SplitPhase.BeforeGuards,
            async () =>
                Assert.IsType<SplitSucceeded>(
                    await Service().ExecuteAsync(user.Id, source.Id, Guid.NewGuid(), input)
                )
        );
        var refused = Assert.IsType<SplitRefused>(
            await service.ExecuteAsync(user.Id, source.Id, Guid.NewGuid(), input)
        );
        Assert.Equal("concurrent_state_changed", refused.Reason);
        Assert.Equal(
            ["accountId", "goalIds", "transactionId"],
            refused.Failures.Single().Facts.Keys.Order()
        );
        var current = Assert.IsType<SplitRefused>(
            await Service().ExecuteAsync(user.Id, source.Id, Guid.NewGuid(), input)
        );
        Assert.Contains(current.Failures, f => f.Reason == "transaction_capacity_exceeded");
        Assert.Contains(current.Failures, f => f.Reason == "account_headroom_exceeded");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitResponseFailure_RecoversSuccessOrReportsUnknownWithoutRewriting(
        bool afterCommit
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(
            afterCommit ? SplitPhase.AfterCommit : SplitPhase.BeforeCommit,
            new IOException("Injected unavailable commit outcome")
        );
        var result = await service.ExecuteAsync(user.Id, source.Id, key, input);
        if (afterCommit)
        {
            var success = Assert.IsType<SplitSucceeded>(result);
            var replay = Assert.IsType<SplitSucceeded>(
                await Service().ExecuteAsync(user.Id, source.Id, key, input)
            );
            Assert.Equal(success.Response.Contributions[0].Id, replay.Response.Contributions[0].Id);
        }
        else
        {
            Assert.IsType<SplitOutcomeUnknown>(result);
            var retry = Assert.IsType<SplitSucceeded>(
                await Service().ExecuteAsync(user.Id, source.Id, key, input)
            );
            Assert.Equal(100, retry.Response.LinkedTotal);
        }
    }

    [Theory]
    [InlineData(SplitPhase.AfterReservation)]
    [InlineData(SplitPhase.AfterGuards)]
    [InlineData(SplitPhase.AfterContributions)]
    [InlineData(SplitPhase.AfterResult)]
    public async Task FailureBetweenSaves_RollsBackEveryRowGuardAndReservation(
        SplitPhase failedPhase
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var second = await GoalFactory.CreateAsync(context, user.Id, name: "Second");
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(
            new(2026, 9, 19),
            [new(goal.Id, 100), new(second.Id, 50)]
        );
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(failedPhase, new IOException("Injected save failure"));
        if (failedPhase == SplitPhase.AfterReservation)
        {
            Assert.IsType<SplitOutcomeUnknown>(
                await service.ExecuteAsync(user.Id, source.Id, key, input)
            );
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() =>
                service.ExecuteAsync(user.Id, source.Id, key, input)
            );
        }
        // A previously observed ordinary writer can still use its versions: failed guards rolled back.
        var ordinary = scope.ServiceProvider.GetRequiredService<GoalContributionService>();
        await ordinary.CreateAsync(
            goal,
            account,
            new PitakaApp.Api.Inputs.CreateGoalContributionInput(null, 25, new(2026, 9, 19))
        );
        var success = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(150, success.Response.LinkedTotal);
        Assert.Equal(175, success.Response.Account.EarmarkedTotal);
        Assert.Equal(2, success.Response.Contributions.Count);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("other-transaction")]
    [InlineData("expense")]
    [InlineData("transfer-out")]
    [InlineData("transfer-in")]
    [InlineData("generated-expense")]
    [InlineData("generated-income")]
    [InlineData("goal-status")]
    [InlineData("goal-target")]
    [InlineData("goal-delete")]
    [InlineData("goal-delete-from-other-account")]
    [InlineData("account-retire")]
    [InlineData("other-account-contribution")]
    public async Task CompetingWriters_InvalidateCapturedSplit(string mutation)
    {
        using var seedScope = factory.Services.CreateScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(seed);
        var account = await AccountFactory.CreateAsync(seed, user.Id, initialBalance: 500);
        var otherAccount = await AccountFactory.CreateAsync(
            seed,
            user.Id,
            name: "Other",
            initialBalance: 500
        );
        var source = await TransactionFactory.CreateAsync(seed, user.Id, account.Id, amount: 500);
        var otherSource = await TransactionFactory.CreateAsync(
            seed,
            user.Id,
            account.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(seed, user.Id);
        var otherGoal = await GoalFactory.CreateAsync(seed, user.Id, name: "Other");
        if (mutation == "goal-delete-from-other-account")
        {
            await seedScope
                .ServiceProvider.GetRequiredService<GoalContributionService>()
                .CreateAsync(goal, otherAccount, new(null, 25, new(2026, 9, 19)));
        }

        if (mutation.StartsWith("generated-"))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await RecurringTransactionFactory.CreateAsync(
                seed,
                user.Id,
                account.Id,
                type: mutation == "generated-expense"
                    ? RecurringTransactionType.Expense
                    : RecurringTransactionType.Income,
                amount: 50,
                startDate: today,
                nextRunDate: today,
                endDate: today
            );
        }
        var input = new LinkedContributionSplitInput(
            new(2026, 9, 19),
            [new(goal.Id, 100, AcknowledgeTargetOverrun: true)]
        );
        var key = Guid.NewGuid();
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.On(
            SplitPhase.BeforeGuards,
            async () =>
            {
                using var scope = factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
                var currentAccount = await context.Accounts.SingleAsync(a => a.Id == account.Id);
                var currentGoal = await context.Goals.SingleAsync(g => g.Id == goal.Id);
                switch (mutation)
                {
                    case "ordinary":
                        await scope
                            .ServiceProvider.GetRequiredService<GoalContributionService>()
                            .CreateAsync(
                                currentGoal,
                                currentAccount,
                                new(null, 25, new(2026, 9, 19))
                            );
                        break;
                    case "other-account-contribution":
                        var other = await context.Accounts.SingleAsync(a =>
                            a.Id == otherAccount.Id
                        );
                        await scope
                            .ServiceProvider.GetRequiredService<GoalContributionService>()
                            .CreateAsync(currentGoal, other, new(null, 25, new(2026, 9, 19)));
                        break;
                    case "other-transaction":
                        Assert.IsType<SplitSucceeded>(
                            await Service()
                                .ExecuteAsync(
                                    user.Id,
                                    otherSource.Id,
                                    Guid.NewGuid(),
                                    input with
                                    {
                                        Contributions = [new(otherGoal.Id, 25)],
                                    }
                                )
                        );
                        break;
                    case "expense":
                    case "transfer-out":
                    case "transfer-in":
                        var from =
                            mutation == "transfer-in"
                                ? await context.Accounts.SingleAsync(a => a.Id == otherAccount.Id)
                                : currentAccount;
                        await scope
                            .ServiceProvider.GetRequiredService<TransactionService>()
                            .CreateAsync(
                                from,
                                new(
                                    mutation == "expense"
                                        ? TransactionType.Expense
                                        : TransactionType.Transfer,
                                    50,
                                    TransferToAccountId: mutation == "expense" ? null
                                        : mutation == "transfer-in" ? account.Id
                                        : otherAccount.Id
                                )
                            );
                        break;
                    case "generated-expense":
                    case "generated-income":
                        await scope
                            .ServiceProvider.GetRequiredService<GenerateDueRecurringTransactions>()
                            .GenerateAsync();
                        break;
                    case "goal-status":
                        await scope
                            .ServiceProvider.GetRequiredService<GoalService>()
                            .PatchStatusAsync(currentGoal, GoalStatus.Completed);
                        break;
                    case "goal-target":
                        await scope
                            .ServiceProvider.GetRequiredService<GoalService>()
                            .UpdateAsync(currentGoal, new(currentGoal.Name, 5, null));
                        break;
                    case "goal-delete":
                    case "goal-delete-from-other-account":
                        await scope
                            .ServiceProvider.GetRequiredService<GoalService>()
                            .DeleteAsync(currentGoal);
                        break;
                    case "account-retire":
                        await scope
                            .ServiceProvider.GetRequiredService<AccountService>()
                            .PatchActiveStatus(currentAccount, new(false));
                        break;
                }
            }
        );
        var refused = Assert.IsType<SplitRefused>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal("concurrent_state_changed", refused.Reason);
        // Acknowledgement never disables version checks; even a target change is a conflict.
        Assert.False(refused.Created);
        var next = await Service()
            .ExecuteAsync(
                user.Id,
                source.Id,
                key,
                input with
                {
                    Contributions = [new(otherGoal.Id, 1)],
                }
            );
        Assert.IsNotType<SplitIdempotencyMismatch>(next);
    }

    [Fact]
    public async Task Replay_SurvivesGoalDeletionAndLaterChangesWithoutRecreatingRows()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(
            new(1999, 12, 31),
            [new(goal.Id, 100, "historical note")]
        );
        var success = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        context.ChangeTracker.Clear();
        var contributionService =
            scope.ServiceProvider.GetRequiredService<GoalContributionService>();
        var created = (await contributionService.GetAllForUser(user)).Single();
        await contributionService.DeleteAsync(created);
        var afterContributionDeletion = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(
            success.Response.Contributions[0],
            afterContributionDeletion.Response.Contributions[0]
        );
        var trackedGoal = (
            await scope
                .ServiceProvider.GetRequiredService<GoalService>()
                .GetTrackedByIdAsync(goal.Id)
        )!;
        await scope.ServiceProvider.GetRequiredService<GoalService>().DeleteAsync(trackedGoal);
        var trackedAccount = (
            await scope
                .ServiceProvider.GetRequiredService<AccountService>()
                .GetTrackedByIdForUserAsync(user, account.Id)
        )!;
        await scope
            .ServiceProvider.GetRequiredService<TransactionService>()
            .CreateAsync(trackedAccount, new(TransactionType.Expense, 25));
        await scope
            .ServiceProvider.GetRequiredService<AccountService>()
            .PatchActiveStatus(trackedAccount, new(false));
        var transactions = scope.ServiceProvider.GetRequiredService<TransactionService>();
        await transactions.DeleteAsync((await transactions.GetTrackedByIdAsync(source.Id))!);
        var replay = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(success.ResponseBody, replay.ResponseBody);
        Assert.Equal(success.Response.Account, replay.Response.Account);
        Assert.Equal(success.Response.Contributions[0], replay.Response.Contributions[0]);
        Assert.Empty(await contributionService.GetAllForUser(user));
        Assert.IsType<SplitIdempotencyMismatch>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    key,
                    input with
                    {
                        ContributionDate = new(2000, 1, 1),
                    }
                )
        );
    }

    [Fact]
    public async Task Ownership_IsResolvedBeforeStateConflictsAndKeysArePerUser()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var firstUser = await UserFactory.CreateAsync(context);
        var secondUser = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, firstUser.Id, initialBalance: 500);
        var otherAccount = await AccountFactory.CreateAsync(
            context,
            secondUser.Id,
            initialBalance: 500
        );
        var source = await TransactionFactory.CreateAsync(
            context,
            firstUser.Id,
            account.Id,
            amount: 500
        );
        var otherSource = await TransactionFactory.CreateAsync(
            context,
            secondUser.Id,
            otherAccount.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(context, firstUser.Id);
        var otherGoal = await GoalFactory.CreateAsync(context, secondUser.Id);
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2100, 1, 1), [new(goal.Id, 100)]);
        var success = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(firstUser.Id, source.Id, key, input)
        );
        var foreignSource = Assert.IsType<SplitRefused>(
            await Service().ExecuteAsync(secondUser.Id, source.Id, key, input)
        );
        var missingSource = Assert.IsType<SplitRefused>(
            await Service().ExecuteAsync(secondUser.Id, int.MaxValue, key, input)
        );
        Assert.Equal("transaction_missing", foreignSource.Reason);
        Assert.Equal(missingSource.Reason, foreignSource.Reason);
        Assert.Equal(["transactionId"], foreignSource.Failures.Single().Facts.Keys);
        var foreignGoal = Assert.IsType<SplitInvalid>(
            await Service().ExecuteAsync(secondUser.Id, otherSource.Id, key, input)
        );
        var missingGoal = Assert.IsType<SplitInvalid>(
            await Service()
                .ExecuteAsync(
                    secondUser.Id,
                    otherSource.Id,
                    key,
                    input with
                    {
                        Contributions = [new(int.MaxValue, 100)],
                    }
                )
        );
        Assert.Equal(
            missingGoal.Errors["contributions[0].goalId"],
            foreignGoal.Errors["contributions[0].goalId"]
        );
        var second = Assert.IsType<SplitSucceeded>(
            await Service()
                .ExecuteAsync(
                    secondUser.Id,
                    otherSource.Id,
                    key,
                    input with
                    {
                        Contributions = [new(otherGoal.Id, 100)],
                    }
                )
        );
        Assert.NotEqual(success.Response.Contributions[0].Id, second.Response.Contributions[0].Id);
    }

    [Theory]
    [InlineData(TransactionType.Expense)]
    [InlineData(TransactionType.Transfer)]
    public async Task IneligibleSourceAndRetiredAccount_CollectOwnedFacts(TransactionType type)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(
            context,
            user.Id,
            initialBalance: 500,
            isActive: false
        );
        var source = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            type,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        var result = Assert.IsType<SplitRefused>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    Guid.NewGuid(),
                    new(new(2026, 9, 19), [new(goal.Id, 1)])
                )
        );
        Assert.Equal(
            ["account_inactive", "transaction_ineligible"],
            result.Failures.Select(f => f.Reason).Order()
        );
        var unavailable = Assert.IsType<SplitInvalid>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    Guid.NewGuid(),
                    new(new(2026, 9, 19), [new(int.MaxValue, 1)])
                )
        );
        Assert.Single(unavailable.Errors);
    }

    [Fact]
    public async Task InterleavedRelease_KeepsOneCoherentSavedSnapshotWithoutConflict()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        var original = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, Guid.NewGuid(), input)
        );
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.On(
            SplitPhase.AfterAccountTotalReader,
            async () =>
            {
                using var releaseScope = factory.Services.CreateScope();
                var contributions =
                    releaseScope.ServiceProvider.GetRequiredService<GoalContributionService>();
                var row = (
                    await contributions.GetByIdForUser(user, original.Response.Contributions[0].Id)
                )!;
                await contributions.DeleteAsync(row);
            }
        );
        var key = Guid.NewGuid();
        var success = Assert.IsType<SplitSucceeded>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(200, success.Response.LinkedTotal);
        Assert.Equal(200, success.Response.Account.EarmarkedTotal);
        Assert.Equal(300, success.Response.RemainingCapacity);
        Assert.Equal(300, success.Response.Account.AvailableHeadroom);
        var replay = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(success.ResponseBody, replay.ResponseBody);
        Assert.Equal(success.Response.Account, replay.Response.Account);
        var next = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, Guid.NewGuid(), input)
        );
        Assert.Equal(200, next.Response.LinkedTotal);
        Assert.Equal(200, next.Response.Account.EarmarkedTotal);
    }

    [Fact]
    public async Task GeneratedIncome_CanBeSplitUsingSubmittedDate()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id);
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await RecurringTransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 500,
            startDate: today,
            nextRunDate: today,
            endDate: today
        );
        await scope
            .ServiceProvider.GetRequiredService<GenerateDueRecurringTransactions>()
            .GenerateAsync();
        var source = (
            await scope
                .ServiceProvider.GetRequiredService<TransactionService>()
                .GetAllForAccountAsync(account)
        ).Single();
        Assert.NotNull(source.RecurringTransactionId);
        var result = Assert.IsType<SplitSucceeded>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    Guid.NewGuid(),
                    new(new(2040, 2, 29), [new(goal.Id, 100)])
                )
        );
        Assert.Equal(new DateOnly(2040, 2, 29), result.Response.Contributions[0].ContributionDate);
        Assert.Equal(500, result.Response.Account.CurrentBalance);
    }

    [Fact]
    public async Task UnavailableReplayLookup_ReturnsUnknownWithoutStartingAWrite()
    {
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(
            SplitPhase.BeforeInitialLookup,
            new IOException("Injected unavailable result lookup")
        );
        var result = await service.ExecuteAsync(
            1,
            1,
            Guid.NewGuid(),
            new(new(2026, 9, 19), [new(1, 1)])
        );
        Assert.IsType<SplitOutcomeUnknown>(result);
        Assert.Equal(0, service.Count(SplitPhase.BeforeReservation));
    }

    [Fact]
    public async Task CommitRecoveryUnavailable_ReturnsUnknownThenReplaysOriginalSuccess()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(SplitPhase.AfterCommit, new IOException("Injected lost commit response"));
        service.Fail(
            SplitPhase.BeforeRecoveryLookup,
            new IOException("Injected unavailable result lookup")
        );
        Assert.IsType<SplitOutcomeUnknown>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        var replay = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(100, replay.Response.LinkedTotal);
        Assert.Single(
            await scope
                .ServiceProvider.GetRequiredService<GoalContributionService>()
                .GetAllForUser(user)
        );
    }

    [Fact]
    public async Task SuccessfulSplit_InvalidatesPreviouslyCapturedOrdinaryCreation()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 150);
        var source = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 150
        );
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        Assert.IsType<SplitSucceeded>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    Guid.NewGuid(),
                    new(new(2026, 9, 19), [new(goal.Id, 100)])
                )
        );
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            scope
                .ServiceProvider.GetRequiredService<GoalContributionService>()
                .CreateAsync(goal, account, new(null, 100, new(2026, 9, 19)))
        );
        var next = Assert.IsType<SplitSucceeded>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    Guid.NewGuid(),
                    new(new(2026, 9, 19), [new(goal.Id, 50)])
                )
        );
        Assert.Equal(0, next.Response.Account.AvailableHeadroom);
        Assert.Equal(0, next.Response.RemainingCapacity);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("overrun")]
    [InlineData("unavailable")]
    public async Task InvalidLaterRow_RefusesEntireSplitAndLeavesKeyReusable(string failure)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
        var source = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 500
        );
        var first = await GoalFactory.CreateAsync(context, user.Id);
        var second = await GoalFactory.CreateAsync(
            context,
            user.Id,
            name: "Second",
            status: failure == "inactive" ? GoalStatus.Completed : GoalStatus.Active,
            targetAmount: failure == "overrun" ? 50 : 10000
        );
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(
            new(2026, 9, 19),
            [new(first.Id, 100), new(failure == "unavailable" ? int.MaxValue : second.Id, 100)]
        );
        var result = await Service().ExecuteAsync(user.Id, source.Id, key, input);
        if (failure == "unavailable")
        {
            Assert.Contains(
                "contributions[1].goalId",
                Assert.IsType<SplitInvalid>(result).Errors.Keys
            );
        }
        else
        {
            var row = Assert.Single(Assert.IsType<SplitRefused>(result).Failures);
            Assert.Equal(1, row.RowIndex);
            Assert.Equal(second.Id, row.GoalId);
        }
        Assert.Empty(
            await scope
                .ServiceProvider.GetRequiredService<GoalContributionService>()
                .GetAllForUser(user)
        );
        Assert.IsType<SplitSucceeded>(
            await Service()
                .ExecuteAsync(
                    user.Id,
                    source.Id,
                    key,
                    input with
                    {
                        Contributions = [new(first.Id, 1)],
                    }
                )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteConnectionUnavailable_ReturnsUnknownBeforeReservation(bool canceled)
    {
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(
            SplitPhase.BeforeWriteConnection,
            canceled
                ? new OperationCanceledException("Injected arbitration deadline")
                : new IOException("Injected write connection failure")
        );
        var result = await service.ExecuteAsync(
            1,
            1,
            Guid.NewGuid(),
            new(new(2026, 9, 19), [new(1, 1)])
        );
        Assert.IsType<SplitOutcomeUnknown>(result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReservationTransportFailure_RecoversWithoutRetryingWrite(
        bool wrappedProviderFailure,
        bool disposalFails
    )
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var (user, account, source, goal) = await SeedSplitAsync(context);
        Exception failure = new IOException("Injected reservation connection failure");
        if (wrappedProviderFailure)
        {
            var unavailable = new MySqlConnectionStringBuilder(
                PitakaWebApplicationFactory.TestConnectionString
            )
            {
                Port = 1,
                ConnectionTimeout = 1,
                Pooling = false,
            };
            await using var connection = new MySqlConnection(unavailable.ConnectionString);
            var providerFailure = await Assert.ThrowsAsync<MySqlException>(() =>
                connection.OpenAsync()
            );
            Assert.True(providerFailure.IsTransient);
            failure = new DbUpdateException("Injected provider failure", providerFailure);
        }
        await using var schedule = new SplitFaultSchedule();
        var service = schedule.CreateOperation();
        service.Fail(SplitPhase.BeforeReservation, failure);
        if (disposalFails)
        {
            service.Fail(
                SplitPhase.AfterWriteDisposal,
                new InvalidOperationException("Injected context disposal failure")
            );
        }
        var key = Guid.NewGuid();
        var input = new LinkedContributionSplitInput(new(2026, 9, 19), [new(goal.Id, 100)]);
        Assert.IsType<SplitOutcomeUnknown>(
            await service.ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(1, service.Count(SplitPhase.BeforeReservation));
        var retry = Assert.IsType<SplitSucceeded>(
            await Service().ExecuteAsync(user.Id, source.Id, key, input)
        );
        Assert.Equal(100, retry.Response.LinkedTotal);
    }

    private static async Task<SplitSeed> SeedSplitAsync(PitakaDbContext context)
    {
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
        var source = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        return new(user, account, source, goal);
    }

    private sealed record SplitSeed(User User, Account Account, Transaction Transaction, Goal Goal);

    private static LinkedContributionSplitService Service() => SplitFaultSchedule.CreateService();
}
