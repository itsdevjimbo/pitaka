namespace PitakaApp.Api.Tests.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Jobs;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

[Collection("Database collection")]
public class GenerateDueRecurringTransactionConcurrencyTest : IDisposable
{
    
    private readonly PitakaWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;

    public GenerateDueRecurringTransactionConcurrencyTest(PitakaWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task ConcurrentGenerateDueRecurringTransaction_SecondOneStillGenerateTransactions()
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        var user = await UserFactory.CreateAsync(_context);
        var staleAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);
        var account2 = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 1000);

        var recurringTransactionA = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, staleAccount.Id, startDate: now.AddDays(-1), nextRunDate: now, name: "Test 1"
        );
        var recurringTransactionB = await RecurringTransactionFactory.CreateAsync(
            _context, user.Id, account2.Id, startDate: now.AddDays(-1), nextRunDate: now, name: "Test 2"
        );

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        
        var contextA = scopeA.ServiceProvider.GetRequiredService<PitakaDbContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<PitakaDbContext>();

        var service = scopeB.ServiceProvider.GetRequiredService<GenerateDueRecurringTransactions>();

        var accountA = await contextA.Accounts.FirstAsync(a => a.Id == staleAccount.Id);
        var accountB = await contextB.Accounts.FirstAsync(a => a.Id == staleAccount.Id);

        accountA.Increase(100);
        await contextA.SaveChangesAsync();

        await service.GenerateAsync();

        using var scopeC = _factory.Services.CreateScope();
        var contextC = scopeC.ServiceProvider.GetRequiredService<PitakaDbContext>();
        Assert.True(await contextC.Transactions.AnyAsync(t => t.RecurringTransactionId == recurringTransactionB.Id));
        Assert.False(await contextC.Transactions.AnyAsync(t => t.RecurringTransactionId == recurringTransactionA.Id));

        var staleRecurringTransaction = await contextC.RecurringTransactions.Where(rt => rt.Id == recurringTransactionA.Id).FirstAsync();
        Assert.Equal(now, staleRecurringTransaction.NextRunDate);
    }
    public void Dispose() => _scope.Dispose();
}