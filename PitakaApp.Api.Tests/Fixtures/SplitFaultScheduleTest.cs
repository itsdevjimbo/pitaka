using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;

namespace PitakaApp.Api.Tests.Fixtures;

[Collection("Database collection")]
public class SplitFaultScheduleTest(PitakaWebApplicationFactory factory)
{
    [Fact]
    public async Task MissingFault_FailsVerificationEvenWhenExecutionReturnsNormally()
    {
        var schedule = new SplitFaultSchedule();
        var operation = schedule.CreateOperation();
        operation.Fail(
            SplitPhase.BeforeReservation,
            new IOException("Must not silently miss this fault")
        );
        Assert.IsType<SplitInvalid>(
            await operation.ExecuteAsync(1, 1, Guid.NewGuid(), new(new(2026, 9, 19), []))
        );

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            schedule.DisposeAsync().AsTask()
        );
        Assert.Contains(
            failure.InnerExceptions,
            e => e.Message.Contains("BeforeReservation exactly once; observed 0")
        );
    }

    [Fact]
    public async Task CallbackFailure_IsReportedEvenWhenCommitRecoveryCatchesIt()
    {
        using var scope = factory.Services.CreateScope();
        var (userId, transactionId, input) = await SeedAsync(
            scope.ServiceProvider.GetRequiredService<PitakaDbContext>()
        );
        var schedule = new SplitFaultSchedule();
        var operation = schedule.CreateOperation();
        var callbackFailure = new InvalidOperationException("A scenario assertion failed");
        operation.On(SplitPhase.AfterCommit, () => throw callbackFailure);
        try
        {
            // Production catches the callback exception and successfully recovers the committed result.
            Assert.IsType<SplitSucceeded>(
                await operation.ExecuteAsync(userId, transactionId, Guid.NewGuid(), input)
            );
        }
        finally
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() =>
                schedule.DisposeAsync().AsTask()
            );
            Assert.Contains(callbackFailure, failure.InnerExceptions);
        }
    }

    [Fact]
    public async Task EarlyExit_ReleasesEveryPauseBeforeJoiningContendingOperations()
    {
        using var scope = factory.Services.CreateScope();
        var (userId, transactionId, input) = await SeedAsync(
            scope.ServiceProvider.GetRequiredService<PitakaDbContext>()
        );
        var key = Guid.NewGuid();
        Task<LinkedContributionSplitResult>? first = null;
        Task<LinkedContributionSplitResult>? second = null;
        var earlyExit = new InvalidOperationException(
            "Scenario failed before releasing its pauses"
        );

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var schedule = new SplitFaultSchedule();
            var contender = schedule.CreateOperation();
            var contenderPaused = contender.PauseAt(SplitPhase.BeforeReservation);
            var winner = schedule.CreateOperation();
            var winnerPaused = winner.PauseAt(SplitPhase.AfterReservation);

            first = contender.ExecuteAsync(userId, transactionId, key, input);
            await contenderPaused.WaitUntilReachedAsync();
            second = winner.ExecuteAsync(userId, transactionId, key, input);
            await winnerPaused.WaitUntilReachedAsync();
            // Cleanup must release the winner too before joining the contender, which needs its key.
            throw earlyExit;
        });

        Assert.Same(earlyExit, failure);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(
            Assert.IsType<SplitSucceeded>(await first).ResponseBody,
            Assert.IsType<SplitSucceeded>(await second).ResponseBody
        );
    }

    private static async Task<(
        int UserId,
        int TransactionId,
        LinkedContributionSplitInput Input
    )> SeedAsync(PitakaDbContext context)
    {
        var user = await UserFactory.CreateAsync(context);
        var account = await AccountFactory.CreateAsync(context, user.Id, initialBalance: 500);
        var transaction = await TransactionFactory.CreateAsync(
            context,
            user.Id,
            account.Id,
            amount: 500
        );
        var goal = await GoalFactory.CreateAsync(context, user.Id);
        return (user.Id, transaction.Id, new(new(2026, 9, 19), [new(goal.Id, 100)]));
    }
}
