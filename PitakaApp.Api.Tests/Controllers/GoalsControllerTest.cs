using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class GoalsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public GoalsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/goals");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirGoals()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await GoalFactory.CreateAsync(_context, userB.Id);
        
        await GoalFactory.CreateAsync(_context, userA.Id, name: "Test goal 1");
        await GoalFactory.CreateAsync(_context, userA.Id, name: "Test goal 2");
        await GoalFactory.CreateAsync(_context, userA.Id, name: "Test goal 3");
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/goals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<GoalWithCurrentAmountResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Get_ReturnsCollectionWithCurrentAmount()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 300);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 500);
        
        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/goals");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<GoalWithCurrentAmountResource>>(TestJsonOptions.Default);
        Assert.Equal(800, body!.First().CurrentAmount);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
        };
        
        var response = await _client.PostAsJsonAsync("/api/goals", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await GoalFactory.CreateAsync(_context, user.Id, "New car");
        _client.ActAsUser(user);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/goals", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        await GoalFactory.CreateAsync(_context, userB.Id, "New car");
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
        };

        var response = await _client.PostAsJsonAsync("/api/goals", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };
        
        var response = await _client.PostAsJsonAsync("/api/goals", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GoalWithCurrentAmountResource>(TestJsonOptions.Default);

        Assert.Equal("New car", body!.Name);
        Assert.Equal(5000, body!.TargetAmount);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)).ToString(), body!.TargetDate.ToString());
        Assert.Equal("Active", body!.Status.ToString());
        Assert.Equal(0, body.CurrentAmount);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_GoalBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);

        var goal = await GoalFactory.CreateAsync(_context, userB.Id);

        var response = await _client.GetAsync("/api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_BelongsToUser_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 300);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 500);

        var response = await _client.GetAsync("/api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GoalWithCurrentAmountResource>(TestJsonOptions.Default);

        Assert.Equal(goal.Id, body!.Id);
        Assert.Equal("Test goal", body!.Name);
        Assert.Equal(10000, body!.TargetAmount);
        Assert.Equal("Active", body!.Status.ToString());
        Assert.Null(body!.TargetDate);
        Assert.Equal(800, body.CurrentAmount);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };

        var response = await _client.PutAsJsonAsync("/api/goals/" + goal.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentGoal_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };

        var response = await _client.PutAsJsonAsync("/api/goals/99999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);
        
        var goal = await GoalFactory.CreateAsync(_context, userB.Id);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };

        var response = await _client.PutAsJsonAsync("/api/goals/" + goal.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    
    [Fact]
    public async Task Update_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await GoalFactory.CreateAsync(_context, user.Id, name: "New car");
        _client.ActAsUser(user);

        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };

        var response = await _client.PutAsJsonAsync("/api/goals/" + goal.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        var account = await AccountFactory.CreateAsync(_context, user.Id);

        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 300);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id, amount: 500);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
            TargetDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
        };

        var response = await _client.PutAsJsonAsync("/api/goals/" + goal.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<GoalWithCurrentAmountResource>(TestJsonOptions.Default);

        Assert.Equal("New car", body!.Name);
        Assert.Equal(5000, body!.TargetAmount);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)), body!.TargetDate);
        Assert.Equal(800, body.CurrentAmount);
    }

    [Fact]
    public async Task Update_KeepingSameName_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id, name: "New car");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "New car",
            TargetAmount = 5000,
        };

        var response = await _client.PutAsJsonAsync("/api/goals/" + goal.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithInvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/goals/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var goal = await GoalFactory.CreateAsync(_context, userB.Id);
        
        _client.ActAsUser(userA);
        

        var response = await _client.DeleteAsync("api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var goal = await GoalFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_EnsuresContributionsAreDeleted()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        var goal = await GoalFactory.CreateAsync(_context, user.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);
        await GoalContributionFactory.CreateAsync(_context, goal.Id, account.Id);

        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/goals/" + goal.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _context.GoalContributions.AsNoTracking().Where(gc => gc.GoalId == goal.Id).ToListAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidBudgetRequests))]
    public async Task Create_WithInvalidData_ReturnsBadRequest(
        string? name, 
        decimal targetAmount
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = name, 
            TargetAmount = targetAmount,
        };


        var response = await _client.PostAsJsonAsync("/api/goals", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    public static IEnumerable<object?[]> InvalidBudgetRequests()
    {
        // Missing name
        yield return new object?[] { null, 100m, };
        // TargetAmount <= 0
        yield return new object?[] { "New car" , -100m};
    }

    public void Dispose() => _scope.Dispose();
}