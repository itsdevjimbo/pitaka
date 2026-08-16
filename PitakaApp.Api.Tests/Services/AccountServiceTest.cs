using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public class AccountServiceTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly AccountService _accountService;

    public AccountServiceTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _accountService = _scope.ServiceProvider.GetRequiredService<AccountService>();
    }

    [Fact]
    public async Task CreateAsync_EnsureCurrentBalanceIsTheSameWithInitialBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var input = new CreateAccountInput(
            Name: "Savings",
            Type: Enums.AccountType.Bank,
            InitialBalance: 5000
        );
        var account = await _accountService.CreateAsync(user, input);

        Assert.Equal(account.InitialBalance, account.CurrentBalance);
    }
    
    
    public void Dispose() => _scope.Dispose();
}