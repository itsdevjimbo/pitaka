using System.Net;
using System.Net.Http.Json;
using Bogus;
using Bogus.DataSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class AccountsControllerTest : IDisposable
{
    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public AccountsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirOwnAccounts()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, userB.Id);
        await AccountFactory.CreateAsync(_context, userA.Id);
        await AccountFactory.CreateAsync(_context, userA.Id);
        await AccountFactory.CreateAsync(_context, userA.Id);
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(TestJsonOptions.Default);
        Assert.All(body!, a => Assert.True(a.UserId == userA.Id));
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsCreated()
    {

        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var request = new
        {
            Name =  "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_RequestWithExistingAccountName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id, "Savings account");

        _client.ActAsUser(user);
        
        var request = new
        {
            Name =  "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Show_InvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_UserNotOwnedAccount_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithOwnedAccount_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidAccountId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);
        var request = new 
        {
            Name = "Savings account"
        };

        var response = await _client.PutAsJsonAsync("/api/accounts/9999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithAnotherUserOwnedAccount_ReturnsForBid()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);
        var request = new 
        {
            Name = "Savings account"
        };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithExistingName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, "Allowance account");
        await AccountFactory.CreateAsync(_context, user.Id, "Savings account");

        _client.ActAsUser(user);

        var request = new 
        {
            Name = "Savings account"
        };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithValidRequest_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, "Allowance account");

        _client.ActAsUser(user);

        var request = new 
        {
            Name = "Savings account"
        };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(TestJsonOptions.Default);
        Assert.Equal("Savings account", body!.Name);
    }


    [Fact]
    public async Task Delete_WithInvalidAccountId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/9999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUserAccount_ReturnsForBid()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnedAccount_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var exists = await _context.Accounts.AsNoTracking().AnyAsync(a => a.Id == account.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task Delete_WithTransactionHistory_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_HasBeenReferenceToATransaction_ReturnsConflict()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);
        await TransactionFactory.CreateAsync(_context, userB.Id, accountB.Id, TransactionType.Transfer, amount: 100, transferToAccountId: accountA.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("/api/accounts/" + accountA.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}