namespace PitakaApp.Api.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

[Collection("Database collection")]
public class BudgetsControllerTest : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public BudgetsControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task Get_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsTheirBudgets()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await AccountFactory.CreateAsync(_context, userB.Id);
        
        await AccountFactory.CreateAsync(_context, userA.Id);
        await AccountFactory.CreateAsync(_context, userA.Id);
        await AccountFactory.CreateAsync(_context, userA.Id);
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<BudgetResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }
    public void Dispose() => _scope.Dispose();
}