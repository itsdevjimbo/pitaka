namespace PitakaApp.Api.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
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
    }

    [Fact]
    public async Task Get_WithNoLoggedinUser_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            Name = "Test category",
            Type = CategoryType.Expense,
        };
        
        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test category 2",
            Type = CategoryType.Expense,
        };
        
        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCorrectCategoryType()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test category 3",
            Type = CategoryType.Expense,
        };
        
        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal("Expense", body!.Type.ToString());
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var category = await CategoryFactory.CreateAsync(_context);

        var response = await _client.GetAsync("/api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_CategoryBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);

        var seededCategory = await CategoryFactory.CreateAsync(_context, userB.Id);

        var response = await _client.GetAsync("/api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_CategoryDoesntBelongToUser_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var seededCategory = await CategoryFactory.CreateAsync(_context);

        var response = await _client.GetAsync("/api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Show_CategoryBelongsToUser_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Test category 3",
            Type = CategoryType.Expense,
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithInvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test category 3",
            Type = CategoryType.Expense,
        };

        var response = await _client.PutAsJsonAsync("/api/categories/99999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OtherUsersCategory_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);
        
        var seededCategory = await CategoryFactory.CreateAsync(_context, userB.Id);

        var request = new
        {
            Name = "Test category 3",
            Type = CategoryType.Expense,
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Test category 3",
            Type = CategoryType.Expense,
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal("Test category 3", body!.Name);
    }
    
    public void Dispose() => _scope.Dispose();
}