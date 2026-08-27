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

        await BudgetFactory.CreateAsync(_context, userB.Id);
        
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 1");
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 2");
        await BudgetFactory.CreateAsync(_context, userA.Id, name: "Test budget 3");
        
        _client.ActAsUser(userA);

        var response = await _client.GetAsync("/api/budgets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<BudgetResource>>(TestJsonOptions.Default);
        Assert.Equal(3, body!.Count);
    }

    [Fact]
    public async Task Create_WithNoLoggedInUser_ReturnsUnauthorized()
    {
        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = 99999
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithOtherUserCategory_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        var category = await CategoryFactory.CreateAsync(_context, userB.Id);
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, user.Id, "Transpo Budget");
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, userB.Id, "Transpo Budget");
        _client.ActAsUser(userA);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithLoggedInUser_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);

        Assert.Equal("Transpo Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).ToString(), body!.StartDate.ToString());
        Assert.Null(body!.EndDate);
        Assert.Null(body!.Description);
        Assert.Null(body!.CategoryId);
    }

    [Fact]
    public async Task Create_UserCategory_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_SystemDefaultCategory_ReturnsCreatedStatusCode()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Transpo Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id,
        };
        
        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Show_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Show_BudgetBelongsToOtherUser_ReturnsNotFound()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);

        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Show_BelongsToUser_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.GetAsync("/api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);

        Assert.Equal("Test budget", body!.Name);
        Assert.Equal(10000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).ToString(), body!.StartDate.ToString());
        Assert.Null(body!.EndDate);
        Assert.Null(body!.Description);
        Assert.Null(body!.CategoryId);
    }

    [Fact]
    public async Task Update_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentBudget_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/99999", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);
        
        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    
    [Fact]
    public async Task Update_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        await BudgetFactory.CreateAsync(_context, user.Id, name: "Test duplicate");
        _client.ActAsUser(user);

        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Test duplicate",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentCategory_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = 9999
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithOtherUserCategory_ReturnsBadRequest()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);
        
        var budget = await BudgetFactory.CreateAsync(_context, userA.Id);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = category.Id
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now.AddDays(3)),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            CategoryId = category.Id,
            Description = "Test update budget"
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);
        Assert.Equal("Updated Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(3)).ToString(), body!.StartDate.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)).ToString(), body!.EndDate.ToString());
        Assert.Equal("Test update budget", body!.Description);
        Assert.Equal(category.Id, body!.CategoryId);
    }

    [Fact]
    public async Task Update_CategoryIdToNull_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id, categoryId: category.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now.AddDays(3)),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            Description = "Test update budget"
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<BudgetResource>(TestJsonOptions.Default);
        Assert.Equal("Updated Budget", body!.Name);
        Assert.Equal(5000, body!.AmountLimit);
        Assert.Equal("Weekly", body!.Period.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(3)).ToString(), body!.StartDate.ToString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now.AddDays(7)).ToString(), body!.EndDate.ToString());
        Assert.Equal("Test update budget", body!.Description);
        Assert.Null(body!.CategoryId);
    }

    [Fact]
    public async Task Update_KeepingSameName_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id, name: "Test same name");

        _client.ActAsUser(user);

        var request = new
        {
            Name = "Test same name",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithInvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/budgets/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersBudget_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var budget = await BudgetFactory.CreateAsync(_context, userB.Id);
        
        _client.ActAsUser(userA);
        

        var response = await _client.DeleteAsync("api/budgets/" + budget.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var seededCategory = await BudgetFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/budgets/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidBudgetRequests))]
    public async Task Create_WithInvalidData_ReturnsBadRequest(
        string? name, 
        decimal amountLimit, 
        BudgetPeriod? period
    )
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = name, 
            AmountLimit = amountLimit, 
            Period = period, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = "Test budget", 
            AmountLimit = 5000, 
            Period = BudgetPeriod.Weekly, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSameStartDateAndEndDate_ReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var request = new { 
            Name = "Test budget", 
            AmountLimit = 5000, 
            Period = BudgetPeriod.Weekly, 
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now),
        };


        var response = await _client.PostAsJsonAsync("/api/budgets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithEndDateBeforeStartDate_ReturnsBadRequest()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var budget = await BudgetFactory.CreateAsync(_context, user.Id);
        
        _client.ActAsUser(user);

        var request = new
        {
            Name = "Updated Budget",
            AmountLimit = 5000,
            Period = BudgetPeriod.Weekly,
            StartDate = DateOnly.FromDateTime(DateTime.Now),
            EndDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
        };

        var response = await _client.PutAsJsonAsync("/api/budgets/" + budget.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object?[]> InvalidBudgetRequests()
    {
        // Missing name
        yield return new object?[] { null, 100m, BudgetPeriod.Weekly };
        // AmountLimit <= 0
        yield return new object?[] { "Test Budget", -100m, BudgetPeriod.Weekly} ;
        // // missing Period
        yield return new object?[] { "Test Budget", 100m, null };
    }

    public void Dispose() => _scope.Dispose();
}

