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

namespace PitakaApp.Api.Tests.Controllers;

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
    public async Task Get_WithNoLoggedInUser_ReturnsSystemDefaults()
    {
        var response = await _client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<CategoryResource>>(TestJsonOptions.Default);
        Assert.NotEmpty(body!);
        Assert.All(body!, c => Assert.True(c.IsDefault));
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
    public async Task Create_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        await CategoryFactory.CreateAsync(_context, user.Id, name: "Groceries");

        var request = new
        {
            Name = "Groceries",
            Type = CategoryType.Expense,
        };

        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameDifferentUser_ReturnsCreated()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);

        await CategoryFactory.CreateAsync(_context, userA.Id, name: "Groceries");

        _client.ActAsUser(userB);

        var request = new
        {
            Name = "Groceries",
            Type = CategoryType.Expense,
        };

        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithStrayParentId_IgnoresItAndReturnsCreated()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var other = await CategoryFactory.CreateAsync(_context, user.Id, name: "Bills");

        var request = new
        {
            Name = "Electricity",
            Type = CategoryType.Expense,
            ParentId = other.Id,
        };

        var response = await _client.PostAsJsonAsync("/api/categories", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal("Electricity", body!.Name);
        Assert.Equal(CategoryType.Expense, body.Type);
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
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_SystemDefaultCategory_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await _context.Categories.Where(c => c.IsDefault).FirstAsync();

        var request = new
        {
            Name = "Test category 3",
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + category.Id, request);
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
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal("Test category 3", body!.Name);
    }

    [Fact]
    public async Task Update_WithStrayType_IgnoresItAndReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);

        var request = new
        {
            Name = "Renamed",
            Type = "Income",
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal(CategoryType.Expense, body!.Type);
    }

    [Fact]
    public async Task Update_OmittingType_LeavesStoredTypeUnchanged()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id, type: CategoryType.Expense);

        var request = new
        {
            Name = "Renamed",
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.Equal(CategoryType.Expense, body!.Type);
    }

    [Fact]
    public async Task Update_DuplicateNameForUser_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        await CategoryFactory.CreateAsync(_context, user.Id, name: "Groceries");
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id, name: "Rent");

        var request = new
        {
            Name = "Groceries",
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_KeepingSameName_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id, name: "Groceries");

        var request = new
        {
            Name = "Groceries",
        };

        var response = await _client.PutAsJsonAsync("/api/categories/" + seededCategory.Id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithInvalidId_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.DeleteAsync("api/categories/99999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersCategory_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(userA);
        
        var seededCategory = await CategoryFactory.CreateAsync(_context, userB.Id);

        var response = await _client.DeleteAsync("api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);
        
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var response = await _client.DeleteAsync("api/categories/" + seededCategory.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithTransactionFiledUnderIt_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, categoryId: category.Id);

        var response = await _client.DeleteAsync("api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithBudgetNarrowedToIt_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        await BudgetFactory.CreateAsync(_context, user.Id, categoryId: category.Id);

        var response = await _client.DeleteAsync("api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithRecurringTransactionCarryingIt_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await RecurringTransactionFactory.CreateAsync(_context, user.Id, account.Id, categoryId: category.Id);

        var response = await _client.DeleteAsync("api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Patch_StatusWithoutLoggedInUser_ReturnsUnauthorized()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_NonExistentCategoryStatus_ReturnsNotFound()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/categories/999999/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_OtherUsersCategoryStatus_ReturnsForbidden()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, userB.Id);

        _client.ActAsUser(userA);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var stored = await _context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Patch_SystemDefaultCategoryStatus_ReturnsForbidden()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_RetireOwnedCategory_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.False(body!.IsActive);

        var stored = await _context.Categories.AsNoTracking().SingleAsync(c => c.Id == category.Id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Patch_ReactivateCategory_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        var category = await CategoryFactory.CreateAsync(_context, user.Id, isActive: false);

        _client.ActAsUser(user);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CategoryResource>(TestJsonOptions.Default);
        Assert.True(body!.IsActive);
    }

    [Fact]
    public async Task Patch_RetiringCategoryInUse_ReturnsOk()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, categoryId: category.Id);

        var response = await _client.PatchAsJsonAsync("/api/categories/" + category.Id + "/status", new { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_IncludesRetiredCategories()
    {
        var user = await UserFactory.CreateAsync(_context);
        var retired = await CategoryFactory.CreateAsync(_context, user.Id, name: "Old Gym", isActive: false);

        _client.ActAsUser(user);

        var response = await _client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<CategoryResource>>(TestJsonOptions.Default);
        var seen = body!.Single(c => c.Id == retired.Id);
        Assert.False(seen.IsActive);
    }

    [Fact]
    public async Task Delete_RetiredCategoryInUse_ReturnsConflict()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id, isActive: false);
        var account = await AccountFactory.CreateAsync(_context, user.Id);
        await TransactionFactory.CreateAsync(_context, user.Id, account.Id, categoryId: category.Id);

        var response = await _client.DeleteAsync("api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RetiredCategoryNotInUse_ReturnsNoContent()
    {
        var user = await UserFactory.CreateAsync(_context);
        _client.ActAsUser(user);

        var category = await CategoryFactory.CreateAsync(_context, user.Id, isActive: false);

        var response = await _client.DeleteAsync("api/categories/" + category.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public void Dispose() => _scope.Dispose();
}