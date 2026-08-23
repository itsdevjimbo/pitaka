using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions;

[Collection("Database collection")]
public class GetDueRecurringTransactionsTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly GetDueRecurringTransactions _getDueRecurringTransactions;

    public GetDueRecurringTransactionsTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _getDueRecurringTransactions = _scope.ServiceProvider.GetRequiredService<GetDueRecurringTransactions>();
    }

    [Fact]
    public async Task Get_IncludesDueInThePast()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.Contains(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_IncludesDueToday()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.Contains(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_BelongsToMultipleUsers_ReturnsAll()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id);

        var userB = await UserFactory.CreateAsync(_context);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);

        var userC = await UserFactory.CreateAsync(_context);
        var accountC = await AccountFactory.CreateAsync(_context, userC.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransactionA = await RecurringTransactionFactory.CreateAsync(
            _context, userA.Id, accountA.Id, startDate: startDate, nextRunDate: startDate
        );
        var recurringTransactionB = await RecurringTransactionFactory.CreateAsync(
            _context, userB.Id, accountB.Id, startDate: startDate, nextRunDate: startDate
        );
        var recurringTransactionC = await RecurringTransactionFactory.CreateAsync(
            _context, userC.Id, accountC.Id, startDate: startDate, nextRunDate: startDate
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.Contains(dueRecurringTransactions, drt => drt.Id == recurringTransactionA.Id);
        Assert.Contains(dueRecurringTransactions, drt => drt.Id == recurringTransactionB.Id);
        Assert.Contains(dueRecurringTransactions, drt => drt.Id == recurringTransactionC.Id);
    }

    [Fact]
    public async Task Get_ExcludesDueTomorrow()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.DoesNotContain(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_ExcludesStatusPaused()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate, status: RecurringTransactionStatus.Paused
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.DoesNotContain(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_ExcludesStatusCompleted()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate, status: RecurringTransactionStatus.Completed
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.DoesNotContain(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_ExcludesStatusCancelled()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate, status: RecurringTransactionStatus.Cancelled
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.DoesNotContain(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    [Fact]
    public async Task Get_ExcludesInactiveAccount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, isActive: false);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: startDate, nextRunDate: startDate
        );

        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();

        Assert.DoesNotContain(dueRecurringTransactions, drt => drt.Id == recurringTransaction.Id);
    }

    public void Dispose() => _scope.Dispose();
}