using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Services;
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

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.All(body!, a => Assert.True(a.UserId == userA.Id));
    }

    [Fact]
    public async Task Get_WithTypeFilter_ReturnsOnlyAccountsOfThatType()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Checking", AccountType.Bank);
        await AccountFactory.CreateAsync(_context, user.Id, "Cash", AccountType.Cash);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?type=Bank");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Checking"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithActiveFilter_ReturnsOnlyActiveAccounts()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Active", isActive: true);
        await AccountFactory.CreateAsync(_context, user.Id, "Retired", isActive: false);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?isActive=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Active"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithRetiredFilter_ReturnsOnlyRetiredAccounts()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Active", isActive: true);
        await AccountFactory.CreateAsync(_context, user.Id, "Retired", isActive: false);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?isActive=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Retired"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithTypeAndActiveFilters_ReturnsTheirIntersection()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Active bank",
            AccountType.Bank,
            isActive: true
        );
        await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Retired bank",
            AccountType.Bank,
            isActive: false
        );
        await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Active cash",
            AccountType.Cash,
            isActive: true
        );

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?type=Bank&isActive=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Active bank"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithFilterMatchingNothing_ReturnsEmptyList()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id, type: AccountType.Cash);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?type=Investment");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Empty(body!);
    }

    [Fact]
    public async Task Get_WithoutFilters_ReturnsActiveAndRetiredAccounts()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Active", isActive: true);
        await AccountFactory.CreateAsync(_context, user.Id, "Retired", isActive: false);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Active", "Retired"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithFilter_ReturnsAccountsOrderedByNameAscending()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Zephyr", AccountType.Bank);
        await AccountFactory.CreateAsync(_context, user.Id, "Apricot", AccountType.Bank);
        await AccountFactory.CreateAsync(_context, user.Id, "Mango", AccountType.Cash);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?type=Bank");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Apricot", "Zephyr"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithFilters_ReturnsOnlyTheLoggedInUsersAccounts()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(
            _context,
            userA.Id,
            "Mine",
            AccountType.Bank,
            isActive: true
        );
        await AccountFactory.CreateAsync(
            _context,
            userB.Id,
            "Theirs",
            AccountType.Bank,
            isActive: true
        );

        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/accounts?type=Bank&isActive=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Mine"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_WithUnparseableType_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?type=nonsense");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithNumericActiveFilter_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts?isActive=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsAccountsOrderedByNameAscending()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Zephyr");
        await AccountFactory.CreateAsync(_context, user.Id, "Apricot");
        await AccountFactory.CreateAsync(_context, user.Id, "Mango");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Apricot", "Mango", "Zephyr"], body!.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_InactiveAccountSortsAmongActiveAccountsByName()
    {
        var user = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, user.Id, "Charlie");
        await AccountFactory.CreateAsync(_context, user.Id, "Bravo", isActive: false);
        await AccountFactory.CreateAsync(_context, user.Id, "Alpha");

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<AccountResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(["Alpha", "Bravo", "Charlie"], body!.Select(a => a.Name));
        Assert.False(body!.Single(a => a.Name == "Bravo").IsActive);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("type")]
    public async Task Create_WithoutRequiredField_ReturnsBadRequestAndCreatesNothing(string omitted)
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        // Every non-defaulted constructor parameter is mandatory in the body once
        // RespectRequiredConstructorParameters is on: a missing key is a 400. Before it, a
        // missing `type` bound to default(AccountType) == Cash and an account was created.
        // See issue #82.
        var request = new Dictionary<string, object>
        {
            ["name"] = "Savings account",
            ["type"] = AccountType.Bank.ToString(),
            ["initialBalance"] = 5000,
        };
        request.Remove(omitted);

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.False(await _context.Accounts.AsNoTracking().AnyAsync(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task Create_RequestWithExistingAccountName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, user.Id, "Savings account");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        await AccountFactory.CreateAsync(_context, userB.Id, "Savings account");
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.True(
            await _context
                .Accounts.AsNoTracking()
                .AnyAsync(a => a.Id == body!.Id && a.UserId == userA.Id)
        );
    }

    [Fact]
    public async Task Create_WithInitialBalance_ReturnsItInResponse()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Savings account",
            Type = AccountType.Bank,
            InitialBalance = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal(5000, body!.InitialBalance);
    }

    [Fact]
    public async Task Create_WithoutInitialBalance_DefaultsToZero()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { Name = "Wallet", Type = AccountType.Cash };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal(0, body!.InitialBalance);
        Assert.Equal(0, body.CurrentBalance);
    }

    [Fact]
    public async Task Create_WithNegativeInitialBalance_IsAccepted()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Overdrawn account",
            Type = AccountType.Bank,
            InitialBalance = -1200,
        };

        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal(-1200, body!.InitialBalance);
    }

    [Theory]
    [InlineData("\"CreditCard\"")]
    [InlineData("2")]
    public async Task Create_WithRemovedCreditCardType_ReturnsBadRequestAndCreatesNothing(
        string typeJson
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var json = $$"""
            {
              "name": "Rewards card",
              "type": {{typeJson}},
              "initialBalance": 0
            }
            """;

        var response = await _client.PostAsync(
            "/api/accounts",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await _context.Accounts.AsNoTracking().AnyAsync(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task Show_ReturnsInitialBalanceDistinctFromCurrentBalance()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);

        account.Decrease(1500);
        await _context.SaveChangesAsync();

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal(5000, body!.InitialBalance);
        Assert.Equal(3500, body.CurrentBalance);
    }

    [Fact]
    public async Task Update_WithInitialBalanceInBody_DoesNotChangeIt()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            "Allowance account",
            initialBalance: 5000
        );

        _client.ActAsUser(user);

        var request = new { Name = "Savings account", InitialBalance = 999 };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal("Savings account", body!.Name);
        Assert.Equal(5000, body.InitialBalance);

        var stored = await _context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal(5000, stored.InitialBalance);
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
        var request = new { Name = "Savings account" };

        var response = await _client.PutAsJsonAsync("/api/accounts/9999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithAnotherUserOwnedAccount_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var account = await AccountFactory.CreateAsync(_context, userB.Id, "Allowance account");

        _client.ActAsUser(userA);
        var request = new { Name = "Savings account" };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await _context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal("Allowance account", stored.Name);
    }

    [Fact]
    public async Task Update_WithExistingName_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, "Allowance account");
        await AccountFactory.CreateAsync(_context, user.Id, "Savings account");

        _client.ActAsUser(user);

        var request = new { Name = "Savings account" };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithValidRequest_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, "Allowance account");

        _client.ActAsUser(user);

        var request = new { Name = "Savings account" };

        var response = await _client.PutAsJsonAsync("/api/accounts/" + account.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.Equal("Savings account", body!.Name);
    }

    [Fact]
    public async Task Patch_ActiveStatusWithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        var request = new { IsActive = false };

        var response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + account.Id + "/status",
            request
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_NonExistentAccountActiveStatus_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { IsActive = false };

        var response = await _client.PatchAsJsonAsync("/api/accounts/999999/status", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_OtherUserAccountActiveStatus_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var request = new { IsActive = false };

        var response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + accountB.Id + "/status",
            request
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await _context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountB.Id);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Patch_ActiveStatus_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var request = new { IsActive = false };

        var response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + account.Id + "/status",
            request
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.False(body!.IsActive);
    }

    [Fact]
    public async Task Patch_ReactvateStatus_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + account.Id + "/status",
            new { IsActive = false }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AccountResource>(
            TestJsonOptions.Default
        );
        Assert.False(body!.IsActive);

        response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + account.Id + "/status",
            new { IsActive = true }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        body = await response.Content.ReadFromJsonAsync<AccountResource>(TestJsonOptions.Default);
        Assert.True(body!.IsActive);
    }

    [Fact]
    public async Task Patch_ActiveStatusWithEmptyBody_ReturnsBadRequestAndLeavesStatusUnchanged()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        // An empty body leaves IsActive unspecified. Before RespectRequiredConstructorParameters
        // it bound to default(bool) == false and retired the account. See issue #82.
        var response = await _client.PatchAsJsonAsync(
            "/api/accounts/" + account.Id + "/status",
            new { }
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stored = await _context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.True(stored.IsActive);
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
    public async Task Delete_OtherUserAccount_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var exists = await _context.Accounts.AsNoTracking().AnyAsync(a => a.Id == account.Id);
        Assert.True(exists);
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
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        var accountBefore = await ReadAccountStateAsync(account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        await AssertConflictAsync(
            response,
            "This account has transaction history and cannot be deleted."
        );

        await AssertAccountUnchangedAsync(account.Id, accountBefore);
        Assert.True(
            await _context.Transactions.AsNoTracking().AnyAsync(t => t.Id == transaction.Id)
        );
    }

    [Fact]
    public async Task Delete_AfterGeneratedTransactionRemoved_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id
        );
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            recurringTransactionId: recurringTransaction.Id
        );
        var transactionService = _scope.ServiceProvider.GetRequiredService<TransactionService>();
        await transactionService.DeleteAsync(transaction);
        var accountBefore = await ReadAccountStateAsync(account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);

        await AssertConflictAsync(
            response,
            "This account has recurring transactions with generated history and cannot be deleted."
        );

        await AssertAccountUnchangedAsync(account.Id, accountBefore);
        Assert.True(
            await _context
                .RecurringTransactions.AsNoTracking()
                .AnyAsync(rt => rt.Id == recurringTransaction.Id && rt.HasGeneratedTransactions)
        );
    }

    [Fact]
    public async Task Delete_HasBeenReferenceToATransaction_ReturnsConflict()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id);
        await TransactionFactory.CreateAsync(
            _context,
            userB.Id,
            accountB.Id,
            TransactionType.Transfer,
            amount: 100,
            transferToAccountId: accountA.Id
        );
        var accountBefore = await ReadAccountStateAsync(accountA.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("/api/accounts/" + accountA.Id);
        await AssertConflictAsync(
            response,
            "This account has transaction history and cannot be deleted."
        );

        await AssertAccountUnchangedAsync(accountA.Id, accountBefore);
        Assert.True(
            await _context
                .Transactions.AsNoTracking()
                .AnyAsync(t => t.TransferToAccountId == accountA.Id)
        );
    }

    [Fact]
    public async Task Delete_HasContributions_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var contributions = new[]
        {
            await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id),
            await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id),
            await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id),
        };
        var accountBefore = await ReadAccountStateAsync(account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/accounts/" + account.Id);
        await AssertConflictAsync(
            response,
            "This account contains funds allocated toward a specific goal."
        );

        await AssertAccountUnchangedAsync(account.Id, accountBefore);
        var contributionIds = await _context
            .GoalContributions.AsNoTracking()
            .Where(gc => gc.AccountId == account.Id)
            .OrderBy(gc => gc.Id)
            .Select(gc => gc.Id)
            .ToListAsync();
        Assert.Equal(contributions.Select(gc => gc.Id), contributionIds);
    }

    [Fact]
    public async Task Delete_WhenTransactionContributionAndGeneratedHistoryExist_ReturnsTransactionConflictFirst()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id
        );
        await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            recurringTransactionId: recurringTransaction.Id
        );
        var accountBefore = await ReadAccountStateAsync(account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        await AssertConflictAsync(
            response,
            "This account has transaction history and cannot be deleted."
        );

        await AssertAccountUnchangedAsync(account.Id, accountBefore);
        Assert.True(
            await _context
                .GoalContributions.AsNoTracking()
                .AnyAsync(gc => gc.AccountId == account.Id)
        );
        Assert.True(
            await _context
                .RecurringTransactions.AsNoTracking()
                .AnyAsync(rt => rt.Id == recurringTransaction.Id && rt.HasGeneratedTransactions)
        );
    }

    [Fact]
    public async Task Delete_WhenContributionAndGeneratedHistoryExist_ReturnsContributionConflictFirst()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var recurringTransaction = await RecurringTransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id
        );
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            recurringTransactionId: recurringTransaction.Id
        );
        var transactionService = _scope.ServiceProvider.GetRequiredService<TransactionService>();
        await transactionService.DeleteAsync(transaction);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        var accountBefore = await ReadAccountStateAsync(account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("/api/accounts/" + account.Id);
        await AssertConflictAsync(
            response,
            "This account contains funds allocated toward a specific goal."
        );

        await AssertAccountUnchangedAsync(account.Id, accountBefore);
        Assert.True(
            await _context.GoalContributions.AsNoTracking().AnyAsync(gc => gc.Id == contribution.Id)
        );
        Assert.True(
            await _context
                .RecurringTransactions.AsNoTracking()
                .AnyAsync(rt => rt.Id == recurringTransaction.Id && rt.HasGeneratedTransactions)
        );
    }

    [Fact]
    public async Task GetTransactions_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        var response = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_WithNonExistentAccount_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/9999/transactions");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_AccountBelongsToOtherUser_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + account.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(
            TestJsonOptions.Default
        );
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task GetTransactions_IncludesTransfersReceivedByTheAccount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id);
        var destination = await AccountFactory.CreateAsync(_context, user.Id);

        var transfer = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            source.Id,
            TransactionType.Transfer,
            amount: 500,
            transferToAccountId: destination.Id
        );

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + destination.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(
            TestJsonOptions.Default
        );
        var received = Assert.Single(body!);
        Assert.Equal(transfer.Id, received.Id);
        Assert.Equal(source.Id, received.AccountId);
        Assert.Equal(destination.Id, received.TransferToAccountId);
    }

    [Fact]
    public async Task GetTransactions_IncludesTransfersSentFromTheAccount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id);
        var destination = await AccountFactory.CreateAsync(_context, user.Id);

        var transfer = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            source.Id,
            TransactionType.Transfer,
            amount: 500,
            transferToAccountId: destination.Id
        );

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + source.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(
            TestJsonOptions.Default
        );
        var sent = Assert.Single(body!);
        Assert.Equal(transfer.Id, sent.Id);
    }

    [Fact]
    public async Task GetTransactions_ExcludesTransfersThatDoNotTouchTheAccount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var source = await AccountFactory.CreateAsync(_context, user.Id);
        var destination = await AccountFactory.CreateAsync(_context, user.Id);
        var bystander = await AccountFactory.CreateAsync(_context, user.Id);

        await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            source.Id,
            TransactionType.Transfer,
            amount: 500,
            transferToAccountId: destination.Id
        );

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/accounts/" + bystander.Id + "/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<TransactionResource>>(
            TestJsonOptions.Default
        );
        Assert.Empty(body!);
    }

    private Task<AccountState> ReadAccountStateAsync(int accountId) =>
        _context
            .Accounts.AsNoTracking()
            .Where(account => account.Id == accountId)
            .Select(account => new AccountState(
                account.UserId,
                account.Name,
                account.Type,
                account.InitialBalance,
                account.CurrentBalance,
                account.IsActive,
                account.Version
            ))
            .SingleAsync();

    private async Task AssertAccountUnchangedAsync(int accountId, AccountState expected)
    {
        Assert.Equal(expected, await ReadAccountStateAsync(accountId));
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string detail)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestJsonOptions.Default
        );
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
    }

    private sealed record AccountState(
        int UserId,
        string Name,
        AccountType Type,
        decimal InitialBalance,
        decimal CurrentBalance,
        bool IsActive,
        uint Version
    );

    public void Dispose() => _scope.Dispose();
}
