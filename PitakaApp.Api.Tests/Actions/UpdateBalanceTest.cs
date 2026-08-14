using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions;

[Collection("Database collection")]
public class UpdateAccountBalanceTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly UpdateAccountBalance _updateAccountBalance;

    public UpdateAccountBalanceTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _updateAccountBalance = _scope.ServiceProvider.GetRequiredService<UpdateAccountBalance>();
    }

    [Fact]
    public async Task ReverseTransaction_TransferWithDanglingTransferToAccountId_Throws()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 3000);

        var transaction = TransactionFactory.Make(
            userId: user.Id, 
            accountId: account.Id, 
            type: TransactionType.Transfer, 
            amount: 1000, 
            transferToAccountId: 99999
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _updateAccountBalance.ReverseTransaction(transaction)
        );
    }

    public void Dispose() => _scope.Dispose();
}