using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Controllers;

[Collection("Database collection")]
public class LinkedContributionsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public LinkedContributionsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/transactions/1/linked-contributions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_MissingOrForeignTransaction_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        var otherUser = await UserFactory.CreateAsync(_context);
        var otherAccount = await AccountFactory.CreateAsync(_context, otherUser.Id);
        var otherTransaction = await TransactionFactory.CreateAsync(
            _context,
            otherUser.Id,
            otherAccount.Id
        );
        _client.ActAsUser(user);

        var missingResponse = await _client.GetAsync(
            "/api/transactions/999999/linked-contributions"
        );
        var foreignResponse = await _client.GetAsync(
            $"/api/transactions/{otherTransaction.Id}/linked-contributions"
        );

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsCompleteCurrentHistoryAndSignedCapacities()
    {
        var user = await UserFactory.CreateAsync(_context);
        var account = await AccountFactory.CreateAsync(
            _context,
            user.Id,
            name: "Retired savings",
            initialBalance: 1000,
            isActive: false
        );
        var completedGoal = await GoalFactory.CreateAsync(
            _context,
            user.Id,
            name: "Completed goal",
            status: GoalStatus.Completed
        );
        var abandonedGoal = await GoalFactory.CreateAsync(
            _context,
            user.Id,
            name: "Abandoned goal",
            status: GoalStatus.Abandoned
        );
        var transaction = await TransactionFactory.CreateAsync(
            _context,
            user.Id,
            account.Id,
            amount: 300
        );
        var first = await GoalContributionFactory.CreateAsync(
            _context,
            completedGoal.Id,
            account.Id,
            transaction.Id,
            amount: 100,
            contributionDate: new DateOnly(2026, 9, 1),
            note: "first"
        );
        var second = await GoalContributionFactory.CreateAsync(
            _context,
            completedGoal.Id,
            account.Id,
            transaction.Id,
            amount: 250,
            contributionDate: new DateOnly(2026, 9, 2)
        );
        await GoalContributionFactory.CreateAsync(
            _context,
            abandonedGoal.Id,
            account.Id,
            amount: 800
        );
        _client.ActAsUser(user);

        var response = await _client.GetAsync(
            $"/api/transactions/{transaction.Id}/linked-contributions"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkedContributionSnapshotResource>(
            TestJsonOptions.Default
        );
        Assert.NotNull(body);
        Assert.Equal(transaction.Id, body.TransactionId);
        Assert.Equal(300, body.TransactionAmount);
        Assert.Equal(350, body.LinkedTotal);
        Assert.Equal(-50, body.RemainingCapacity);
        Assert.Equal(account.Id, body.Account.Id);
        Assert.Equal("Retired savings", body.Account.Name);
        Assert.Equal(1000, body.Account.CurrentBalance);
        Assert.Equal(1150, body.Account.EarmarkedTotal);
        Assert.Equal(-150, body.Account.AvailableHeadroom);
        Assert.False(body.Account.Active);

        Assert.Equal(2, body.LinkedContributions.Count);
        var rows = body.LinkedContributions.ToDictionary(row => row.Id);
        Assert.Equal(completedGoal.Id, rows[first.Id].GoalId);
        Assert.Equal(account.Id, rows[first.Id].AccountId);
        Assert.Equal(transaction.Id, rows[first.Id].TransactionId);
        Assert.Equal(100, rows[first.Id].Amount);
        Assert.Equal(new DateOnly(2026, 9, 1), rows[first.Id].ContributionDate);
        Assert.Equal("first", rows[first.Id].Note);
        Assert.Equal("Completed goal", rows[first.Id].GoalName);
        Assert.Equal(completedGoal.Id, rows[second.Id].GoalId);
        Assert.Equal("Completed goal", rows[second.Id].GoalName);
    }

    public void Dispose() => _scope.Dispose();
}
