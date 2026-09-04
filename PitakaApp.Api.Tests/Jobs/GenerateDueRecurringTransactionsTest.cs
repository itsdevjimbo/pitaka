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
    
    // The ceiling of the decimal(14, 2) columns Amount and CurrentBalance map to.
    private const decimal DecimalColumnCeiling = 999_999_999_999.99m;

    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly GenerateDueRecurringTransactions _generateDueRecurringTransactions;

    // Schedules built to fail on purpose. This collection shares one database with no
    // per-test reset, so they are purged in Dispose — even if an assertion threw first —
    // to stop later tests' GenerateAsync runs tripping over them every tick.
    private readonly List<int> _schedulesToPurge = new();

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

    [Fact]
    public async Task Generate_OneScheduleHasAnUnmappedType_TheRestOfTheRunStillGenerates()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 100);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // Inserted first so it is reached first: an enum value with no case in
        // GenerateTransaction, which throws InvalidOperationException rather than a
        // DbUpdateException. The run must not end on it.
        var failing = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date,
            type: (RecurringTransactionType)99, name: "Unmapped type"
        );
        _schedulesToPurge.Add(failing.Id);

        var healthy = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account.Id, startDate: date.AddDays(-1), nextRunDate: date,
            type: RecurringTransactionType.Income, amount: 300, name: "Healthy"
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        Assert.False(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == failing.Id));
        Assert.True(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == healthy.Id));

        var healthyAfter = await _context.RecurringTransactions.AsNoTracking().FirstAsync(rt => rt.Id == healthy.Id);
        Assert.Equal(date.AddDays(1), healthyAfter.NextRunDate);
    }

    [Fact]
    public async Task Generate_OneScheduleFailsToSave_ItsTrackedStateDoesNotLeakIntoTheNext()
    {
        var user = await UserFactory.CreateAsync(_context);

        // Balance sits at the decimal(14, 2) ceiling; the generated income tips
        // CurrentBalance past the column and SaveChangesAsync throws DbUpdateException
        // after the balance was mutated and the Transaction was already tracked.
        var overflowingAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: DecimalColumnCeiling);
        var healthyAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var failing = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, overflowingAccount.Id, startDate: date.AddDays(-1), nextRunDate: date,
            amount: DecimalColumnCeiling, name: "Overflows"
        );
        _schedulesToPurge.Add(failing.Id);

        var healthy = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, healthyAccount.Id, startDate: date.AddDays(-1), nextRunDate: date,
            amount: 300, name: "Healthy"
        );

        await _generateDueRecurringTransactions.GenerateAsync();

        Assert.True(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == healthy.Id));
        Assert.False(await _context.Transactions.AnyAsync(t => t.RecurringTransactionId == failing.Id));

        // The healthy account moved by exactly its own amount: the failed iteration's
        // balance change did not ride along on this SaveChangesAsync.
        var healthyAfter = await _context.Accounts.AsNoTracking().FirstAsync(a => a.Id == healthyAccount.Id);
        Assert.Equal(800, healthyAfter.CurrentBalance);

        var overflowingAfter = await _context.Accounts.AsNoTracking().FirstAsync(a => a.Id == overflowingAccount.Id);
        Assert.Equal(DecimalColumnCeiling, overflowingAfter.CurrentBalance);
    }

    public void Dispose()
    {
        if (_schedulesToPurge.Count > 0)
        {
            _context.RecurringTransactions.Where(rt => _schedulesToPurge.Contains(rt.Id)).ExecuteDelete();
        }

        _scope.Dispose();
    }
}
