namespace PitakaApp.Api.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Resources;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

[Collection("Database collection")]
public class CategoriesControllerTest : IDisposable
{
    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly HttpClient _client;

    public CategoriesControllerTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _client = factory.CreateClient();   
    }


    [Fact]
    public async Task Get_WithLoggedInUser_ReturnsOk()
    {
        var email = _faker.Internet.Email();

        var user = await UserFactory.CreateAsync(_context, email);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<CategoryResource>>();

        Assert.Equal(17, body!.Count);
    }

    [Fact]
    public async Task Get_WithNoLoggedUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    public void Dispose() => _scope.Dispose();
}