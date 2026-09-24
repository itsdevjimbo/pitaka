using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Controllers;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class RequestCancellationTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public RequestCancellationTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task Transactions_Post_CancelledRequest_PropagatesCancellationToAccountLookup()
    {
        var controller = await CreateControllerForUserAsync<TransactionsController>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Post(
                new CreateTransactionRequest(999, TransactionType.Income, 1),
                new CancellationToken(canceled: true)
            )
        );
    }

    [Fact]
    public async Task Accounts_Show_CancelledRequest_PropagatesCancellationToAccountLookup()
    {
        var controller = await CreateControllerForUserAsync<AccountsController>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Show(999, new CancellationToken(canceled: true))
        );
    }

    [Fact]
    public async Task Accounts_GetTransactions_CancelledRequest_PropagatesCancellationToAccountLookup()
    {
        var controller = await CreateControllerForUserAsync<AccountsController>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.GetTransactions(999, new CancellationToken(canceled: true))
        );
    }

    [Fact]
    public async Task AccountTransactions_CancelledRequest_PropagatesCancellationToTransactionQuery()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var transactionService = _scope.ServiceProvider.GetRequiredService<TransactionService>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transactionService.GetAllForAccountAsync(account, new CancellationToken(canceled: true))
        );
    }

    [Fact]
    public async Task RecurringTransactions_Create_CancelledRequest_PropagatesCancellationToAccountLookup()
    {
        var controller = await CreateControllerForUserAsync<RecurringTransactionsController>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Create(
                new CreateRecurringTransactionRequest(
                    999,
                    "Rent",
                    RecurringTransactionType.Expense,
                    Frequency.Monthly,
                    1,
                    new DateOnly(2027, 1, 1)
                ),
                new CancellationToken(canceled: true)
            )
        );
    }

    [Fact]
    public async Task GoalContributions_Create_CancelledRequest_PropagatesCancellationToAccountLookup()
    {
        var controller = await CreateControllerForUserAsync<GoalContributionsController>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Create(
                new CreateGoalContributionRequest(999, 999, 1, new DateOnly(2026, 9, 24)),
                new CancellationToken(canceled: true)
            )
        );
    }

    private async Task<TController> CreateControllerForUserAsync<TController>()
    {
        var user = await UserFactory.CreateAsync(_context);
        _scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().User = user;
        return ActivatorUtilities.CreateInstance<TController>(_scope.ServiceProvider);
    }

    public void Dispose() => _scope.Dispose();
}
