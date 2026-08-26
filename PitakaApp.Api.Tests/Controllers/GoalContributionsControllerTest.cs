using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class GoalContributionsControllerTest
{
    
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public GoalContributionsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/goal-contributions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirGoalContributions()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 5000);

        var goalA = await GoalFactory.CreateAsync(_context, userA.Id, targetAmount: 10000);
        var goalB = await GoalFactory.CreateAsync(_context, userB.Id, targetAmount: 10000);

        await GoalContributionFactory.CreateAsync(_context, goalB.Id, accountB.Id);

        await GoalContributionFactory.CreateAsync(_context, goalA.Id, accountA.Id);
        await GoalContributionFactory.CreateAsync(_context, goalA.Id, accountA.Id);
        await GoalContributionFactory.CreateAsync(_context, goalA.Id, accountA.Id);
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/goal-contributions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<GoalContributionResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            GoalId = 3,
            AccountId = 3,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentGoal_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = 999,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Goal doesnt exists", responseBody);
    }

    [Fact]
    public async Task Create_WithGoalBelongsToOtherUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, userB.Id, targetAmount: 10000);
        var account = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);

        _client.ActAsUser(userA);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Goal doesnt exists", responseBody);
    }

    [Fact]
    public async Task Create_WithAbandonedGoal_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000, status: GoalStatus.Abandoned);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Cannot make contributions to an abandoned goal", responseBody);
    }

    [Fact]
    public async Task Create_WithNonExistentAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = 999,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Account doesnt exists", responseBody);
    }

    [Fact]
    public async Task Create_WithAccountBelongsToOtherUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, userA.Id, targetAmount: 10000);
        var account = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 5000);

        _client.ActAsUser(userA);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Account doesnt exists", responseBody);
    }

    [Fact]
    public async Task Create_WithInactiveAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000, isActive: false);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Account is inactive", responseBody);
    }

    [Fact]
    public async Task Create_WithExpenseTransaction_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, accountId: account.Id, type: TransactionType.Expense);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Cannot make contribution base on this transaction", responseBody);
    }

    [Fact]
    public async Task Create_WithTransferTransctionNotADestinationAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var targetAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        var transaction = await TransactionFactory.CreateAsync(
            _context, user.Id, accountId: account.Id, type: TransactionType.Transfer, transferToAccountId: targetAccount.Id
        );

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Cannot make contribution base on this transaction", responseBody);
    }

    [Fact]
    public async Task Create_WithTransactionAccountDoesntMatchContributionAccount_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);

        var accountA = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, accountId: accountB.Id);

        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = accountA.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Cannot make contribution base on this transaction", responseBody);
    }


    [Fact]
    public async Task Create_WithTransactionBelongsToOtherUser_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var accountA = await AccountFactory.CreateAsync(_context, userA.Id, initialBalance: 5000);
        var accountB = await AccountFactory.CreateAsync(_context, userB.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, userA.Id, targetAmount: 10000);
        var transaction = await TransactionFactory.CreateAsync(_context, userB.Id, accountId: accountB.Id);

        _client.ActAsUser(userA);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = accountA.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Cannot make contribution base on this transaction", responseBody);
    }

    [Fact]
    public async Task Create_WithAmountExceedsAccountBalance_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 500);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 300);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 300,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.Equal("Contributions cannot exceed the account's balance", responseBody);
    }
    public static IEnumerable<object?[]> InvalidGoalContributionRequests()
    {
        // Missing goalId
        yield return new object?[] { null, 1, 300m, DateOnly.FromDateTime(DateTime.Now) };

        // Missing accountId
        yield return new object?[] { 1, null, 300m, DateOnly.FromDateTime(DateTime.Now) };

        // Invalid amount range
        yield return new object?[] { 1, 1, 0m, DateOnly.FromDateTime(DateTime.Now) };

        // Invalid amount range
        yield return new object?[] { 1, 1, -300m, DateOnly.FromDateTime(DateTime.Now) };

        // Missing amount 
        yield return new object?[] { 1, 1, null, DateOnly.FromDateTime(DateTime.Now) };

        // Missing contribution date
        yield return new object?[] { 1, 1, 300m, null };
    }

    [Theory]
    [MemberData(nameof(InvalidGoalContributionRequests))]
    public async Task Create_WithInvalidData_ReturnsBadRequest(
        int? goalId,
        int? accountId,
        decimal? amount, 
        DateOnly? contributionDate
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            GoalId = goalId,
            AccountId = accountId,
            Amount = amount,
            ContributionDate = contributionDate,
        };


        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoTransaction_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTransaction_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        var transaction = await TransactionFactory.CreateAsync(_context, user.Id, accountId: account.Id);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidTransferTransaction_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var sourceAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var destinationAccount = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);
        var transaction = await TransactionFactory.CreateAsync(
            _context, user.Id, accountId: sourceAccount.Id, type: TransactionType.Transfer, transferToAccountId: destinationAccount.Id
        );

        _client.ActAsUser(user);

        var now = DateOnly.FromDateTime(DateTime.Now);

        var request = new
        {
            GoalId = goal.Id,
            AccountId = destinationAccount.Id,
            Amount = 30,
            TransactionId = transaction.Id,
            ContributionDate = now,
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var goalContribution = await _context.GoalContributions.Where(gc => gc.GoalId == goal.Id).FirstAsync();

        var body = await response.Content.ReadFromJsonAsync<GoalContributionResource>();
        
        Assert.Equal(goalContribution.Id, body!.Id);
        Assert.Equal(goal.Id, body.GoalId);
        Assert.Equal(destinationAccount.Id, body.AccountId);
        Assert.Equal(30, body.Amount);
        Assert.Equal(transaction.Id, body.TransactionId);
        Assert.Equal(now, body.ContributionDate);
        Assert.Null(body.Note);
    }

    [Fact]
    public async Task Create_WithAmountEqualToAccountBalance_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 10000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 5000,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAmountPastGoalTargetAmount_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 2000);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 1000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 2000,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithCompletedGoal_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id, initialBalance: 5000);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, targetAmount: 2000, status: GoalStatus.Completed);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 2000);

        _client.ActAsUser(user);
        
        var request = new
        {
            GoalId = goal.Id,
            AccountId = account.Id,
            Amount = 2000,
            ContributionDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goal-contributions", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        var response = await _client.GetAsync("/api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_NonExistentGoalContribution_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/goal-contributions/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_GoalContributionBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var goal = await GoalFactory.CreateAsync(_context, userB.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    
    [Fact]
    public async Task Show_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GoalContributionResource>();
        
        Assert.Equal(contribution.Id, body!.Id);
        Assert.Equal(contribution.GoalId, body.GoalId);
        Assert.Equal(contribution.AccountId, body.AccountId);
        Assert.Equal(contribution.Amount, body.Amount);
        Assert.Equal(contribution.ContributionDate, body.ContributionDate);
        Assert.Null(body.TransactionId);
        Assert.Null(body.Note);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        var request = new
        {
            ContributionDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-3),
            Note = "Test note"
        };

        var response = await _client.PutAsJsonAsync("/api/goal-contributions/" + contribution.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentGoalContribution_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);

        _client.ActAsUser(user);
        
        var request = new
        {
            ContributionDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-3),
            Note = "Test note"
        };

        var response = await _client.PutAsJsonAsync("/api/goal-contributions/99999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_GoalContributionBelongsToOtherUser_ReturnsUnauthorized()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var goal = await GoalFactory.CreateAsync(_context, userB.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        _client.ActAsUser(userA);
        
        var request = new
        {
            ContributionDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-3),
            Note = "Test note"
        };

        var response = await _client.PutAsJsonAsync("/api/goal-contributions/" + contribution.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        _client.ActAsUser(user);
        
        var contributionDate = DateOnly.FromDateTime(DateTime.Now).AddDays(-3);

        var request = new
        {
            ContributionDate = contributionDate,
            Note = "Test note"
        };

        var response = await _client.PutAsJsonAsync("/api/goal-contributions/" + contribution.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<GoalContributionResource>();

        Assert.Equal(contribution.AccountId, body!.AccountId);
        Assert.Equal(contribution.Amount, body.Amount);
        Assert.Equal(contributionDate, body.ContributionDate);
        Assert.Equal("Test note", body.Note);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        var response = await _client.DeleteAsync("api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistentGoalContribution_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/goal-contributions/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_GoalContributionBelongsToOtherUser_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, userB.Id);
        var goal = await GoalFactory.CreateAsync(_context, userB.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        _client.ActAsUser(userA);

        var response = await _client.DeleteAsync("api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var contribution = await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/goal-contributions/" + contribution.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}