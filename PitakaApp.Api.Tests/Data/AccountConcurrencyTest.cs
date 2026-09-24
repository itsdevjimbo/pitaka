using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
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
    public async Task Delete_ConcurrentContributionCommit_ThrowsConcurrencyConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        await AssertConcurrentWritePreventsAccountDeletionAsync(
            account,
            async () =>
            {
                using var createScope = _factory.Services.CreateScope();
                var createContext =
                    createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
                var service =
                    createScope.ServiceProvider.GetRequiredService<GoalContributionService>();
                var guardedAccount = await createContext.Accounts.SingleAsync(a =>
                    a.Id == account.Id
                );
                var guardedGoal = await createContext.Goals.SingleAsync(g => g.Id == goal.Id);

                await service.CreateAsync(
                    guardedGoal,
                    guardedAccount,
                    new CreateGoalContributionInput(null, 100, new DateOnly(2026, 9, 24))
                );
            },
            async context =>
                await context.GoalContributions.AnyAsync(gc => gc.AccountId == account.Id)
        );
    }

    [Fact]
    public async Task Delete_ConcurrentTransactionCommit_ThrowsConcurrencyConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);

        await AssertConcurrentWritePreventsAccountDeletionAsync(
            account,
            async () =>
            {
                using var createScope = _factory.Services.CreateScope();
                var createContext =
                    createScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
                var service = createScope.ServiceProvider.GetRequiredService<TransactionService>();
                var guardedAccount = await createContext.Accounts.SingleAsync(a =>
                    a.Id == account.Id
                );

                await service.CreateAsync(
                    guardedAccount,
                    new CreateTransactionInput(TransactionType.Income, 100)
                );
            },
            async context => await context.Transactions.AnyAsync(t => t.AccountId == account.Id)
        );
    }

    private async Task AssertConcurrentWritePreventsAccountDeletionAsync(
        PitakaApp.Api.Models.Account account,
        Func<Task> commitWrite,
        Func<PitakaDbContext, Task<bool>> writeExists
    )
    {
        var interceptor = new PauseBeforeAccountDelete();
        var options = new DbContextOptionsBuilder<PitakaDbContext>(
            _scope.ServiceProvider.GetRequiredService<DbContextOptions<PitakaDbContext>>()
        )
            .AddInterceptors(interceptor)
            .Options;
        await using var deleteContext = new PitakaDbContext(options);
        var accountService = new AccountService(deleteContext, new CheckUserScopedNameExists());
        var deleteTask = accountService.DeleteAsync(
            account.UserId,
            account.Id,
            CancellationToken.None
        );

        Exception? deleteFailure = null;
        try
        {
            await interceptor.WaitUntilDeleteIsReachedAsync();
            await commitWrite();
        }
        finally
        {
            interceptor.Release();
            try
            {
                await deleteTask;
            }
            catch (Exception exception)
            {
                deleteFailure = exception;
            }
        }

        Assert.IsType<DbUpdateConcurrencyException>(deleteFailure);

        using var assertScope = _factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.True(await assertContext.Accounts.AnyAsync(a => a.Id == account.Id));
        Assert.True(await writeExists(assertContext));
    }

    private sealed class PauseBeforeAccountDelete : SaveChangesInterceptor
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        private readonly TaskCompletionSource _deleteReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task WaitUntilDeleteIsReachedAsync() => _deleteReached.Task.WaitAsync(Timeout);

        public void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _deleteReached.TrySetResult();
            await _released.Task.WaitAsync(Timeout, cancellationToken);
            return result;
        }
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

    [Fact]
    public async Task TransferRemovalWithStaleDestinationVersion_RollsBackBothBalancesAndRemoval()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Removal source",
            initialBalance: 500
        );
        var destination = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Removal destination",
            initialBalance: 500
        );
        var seedService = _scope.ServiceProvider.GetRequiredService<TransactionService>();
        var transaction = await seedService.CreateAsync(
            source,
            new CreateTransactionInput(
                TransactionType.Transfer,
                100,
                TransferToAccountId: destination.Id
            )
        );

        using var removalScope = _factory.Services.CreateScope();
        using var competingScope = _factory.Services.CreateScope();
        var removalContext = removalScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var competingContext = competingScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var removalService = removalScope.ServiceProvider.GetRequiredService<TransactionService>();
        var transactionToRemove = await removalContext.Transactions.SingleAsync(t =>
            t.Id == transaction.Id
        );
        await removalContext
            .Accounts.Where(a => a.Id == source.Id || a.Id == destination.Id)
            .LoadAsync();

        var competingDestination = await competingContext.Accounts.SingleAsync(a =>
            a.Id == destination.Id
        );
        competingDestination.Increase(25);
        await competingContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            removalService.DeleteAsync(transactionToRemove)
        );

        using var assertScope = _factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.Equal(
            400m,
            (await assertContext.Accounts.SingleAsync(a => a.Id == source.Id)).CurrentBalance
        );
        Assert.Equal(
            625m,
            (await assertContext.Accounts.SingleAsync(a => a.Id == destination.Id)).CurrentBalance
        );
        Assert.True(await assertContext.Transactions.AnyAsync(t => t.Id == transaction.Id));
    }

    public void Dispose() => _scope.Dispose();
}
