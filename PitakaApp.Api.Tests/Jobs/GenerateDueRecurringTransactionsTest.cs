using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Jobs;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Jobs;

[Collection("Database collection")]
public class GenerateDueRecurringTransactionsTest : IDisposable
{
    
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly GenerateDueRecurringTransactions _generateDueRecurringTransactions;

    public GenerateDueRecurringTransactionsTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _generateDueRecurringTransactions = _scope.ServiceProvider.GetRequiredService<GenerateDueRecurringTransactions>();
    }

    [Fact]
    public async Task Generate_DueRecurringTransactionsLandsOnEndDate_RecurringTransactionStaysActive()
    {
        
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date, endDate: date.AddDays(1)
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        await _context.Entry(recurringTransaction).ReloadAsync();

        Assert.True(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == recurringTransaction.Id));
        Assert.Equal(RecurringTransactionStatus.Active, recurringTransaction.Status);
        Assert.Equal(date.AddDays(1), recurringTransaction.NextRunDate);
    }

    [Fact]
    public async Task Generate_DueRecurringTransactionsLandsPastEndDate_RecurringTransactionTransitionToCompleted()
    {
        
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date, endDate: date
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        await _context.Entry(recurringTransaction).ReloadAsync();
        
        Assert.True(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == recurringTransaction.Id));
        Assert.Equal(RecurringTransactionStatus.Completed, recurringTransaction.Status);
        Assert.Equal(date, recurringTransaction.NextRunDate);
    }

    [Fact]
    public async Task Generate_PausedRecurringTransactions_GeneratesNothing()
    {
        
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date, endDate: date.AddDays(1), status: RecurringTransactionStatus.Paused
        );

        await _generateDueRecurringTransactions.GenerateAsync();
        
        Assert.False(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == recurringTransaction.Id));
    }

    [Fact]
    public async Task Generate_OverdueRecurringTransaction_MovesToFutureOccurence()
    {
        
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-7), nextRunDate: date.AddDays(-3)
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        Assert.Single(await _context.Transactions.Where(t => t.RecurringTransactionId == recurringTransaction.Id).ToListAsync());

        var transaction = await _context.Transactions.FirstAsync(t => t.RecurringTransactionId == recurringTransaction.Id);
        Assert.Equal(date.AddDays(-3).ToDateTime(TimeOnly.MinValue), transaction.TransactionDate);

        await _context.Entry(recurringTransaction).ReloadAsync();
        Assert.Equal(date.AddDays(1), recurringTransaction.NextRunDate);
    }

    [Fact]
    public async Task Generate_TwoSchedulesOnSameAccount_BothAppliedToBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-7), nextRunDate: date, amount: 300, name: "Test 1"
        );
        await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-7), nextRunDate: date, amount: 500, name: "Test 2"
        );

        await _generateDueRecurringTransactions.GenerateAsync();
        await _context.Entry(account).ReloadAsync();

        Assert.Equal(1300, account.CurrentBalance);
    }

    [Fact]
    public async Task Generate_TransactionDateKindStaysUnspecified_NotConvertedToUtc()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        var transaction = await _context.Transactions.FirstAsync(t => t.RecurringTransactionId == recurringTransaction.Id);

        // Deliberately not UTC: NextRunDate is a bare DateOnly with no timezone ever attached
        // to it, and this app has no per-user timezone field. Stamping it UTC would make a
        // client correctly convert it to local time on display, landing on the wrong calendar
        // day for every user west of UTC, since there's no real instant here to convert from.
        Assert.Equal(DateTimeKind.Unspecified, transaction.TransactionDate.Kind);
    }

    public void Dispose() => _scope.Dispose();
}