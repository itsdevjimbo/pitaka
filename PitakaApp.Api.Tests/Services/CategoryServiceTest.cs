using Bogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Services;

[Collection("Database collection")]
public class CategoryServiceTest : IDisposable
{    
    private readonly Faker _faker = new();
    private readonly IServiceScope _scope;
    private readonly PitakaDbContext _context;
    private readonly CategoryService _categoryService;

    public CategoryServiceTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
        _categoryService = _scope.ServiceProvider.GetRequiredService<CategoryService>();
    }

    [Fact]
    public async Task GetAll_WithNoUser_ReturnsCorrectLength()
    {
        var categories = await _categoryService.GetSystemDefaults();
        Assert.NotEmpty(categories);
        Assert.All(categories, c => Assert.True(c.IsDefault));
    }

    [Fact]
    public async Task GetAll_WithUser_ReturnsCorrectLength()
    {
        var systemDefaultCategories = await _categoryService.GetSystemDefaults();

        var user = await UserFactory.CreateAsync(_context);
        
        await CategoryFactory.CreateAsync(_context, user.Id);

        var categories = await _categoryService.GetAllForUser(user);
        
        Assert.Equal(systemDefaultCategories.Count + 1, categories.Count);
    }

    [Fact]
    public async Task GetAll_WithCrossUser_ReturnsCorrectLength()
    {
        var systemDefaultCategories = await _categoryService.GetSystemDefaults();

        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        
        await CategoryFactory.CreateAsync(_context, userA.Id);
        await CategoryFactory.CreateAsync(_context, userA.Id);

        await CategoryFactory.CreateAsync(_context, userB.Id);

        var categoriesA = await _categoryService.GetAllForUser(userA);
        Assert.Equal(systemDefaultCategories.Count + 2, categoriesA.Count);
        
        var categoriesB = await _categoryService.GetAllForUser(userB);
        Assert.Equal(systemDefaultCategories.Count + 1, categoriesB.Count);
    }

    [Fact]
    public async Task CreateUserOwnedCategory_ReturnsCategoryIsDefaultFalse()
    {
        var user = await UserFactory.CreateAsync(_context);
        var input = new CreateCategoryInput(Name: "Test category", Type: Enums.CategoryType.Expense);
        var category = await _categoryService.CreateUserOwnedAsync(user, input);

        Assert.NotNull(category);
        Assert.False(category.IsDefault);
    }

    [Fact]
    public async Task GetByIdForUser_WithCorrectUser_ReturnsCategory()
    {
        var user = await UserFactory.CreateAsync(_context);
        var seededCategory = await CategoryFactory.CreateAsync(_context, user.Id);

        var category = await _categoryService.GetByIdForUser(user, seededCategory.Id);
        Assert.NotNull(category);
    }

    [Fact]
    public async Task GetByIdForUser_WithSystemDefault_ReturnsCategory()
    {
        var user = await UserFactory.CreateAsync(_context);
        var seededCategory = await CategoryFactory.CreateAsync(_context);

        var category = await _categoryService.GetByIdForUser(user, seededCategory.Id);
        Assert.NotNull(category);
    }

    [Fact]
    public async Task GetByIdForUser_WithCrossUser_ReturnsNull()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        var seededCategory = await CategoryFactory.CreateAsync(_context, userA.Id);

        var category = await _categoryService.GetByIdForUser(userB, seededCategory.Id);
        Assert.Null(category);
    }

    [Fact]
    public async Task Update_ReturnsCorrectSavedCategory()
    {
        var seededCategory = await CategoryFactory.CreateAsync(_context);
        var input = new UpdateCategoryInput(Name: "Test category", Description: "desc", Icon: "icon");
        var category = await _categoryService.UpdateAsync(seededCategory, input);
        Assert.Equal("Test category", category.Name);
        Assert.Equal("desc", category.Description);
        Assert.Equal("icon", category.Icon);
    }
    
    [Fact]
    public async Task Delete_EnsuresTheEntityIsDeleted()
    {
        var seededCategory = await CategoryFactory.CreateAsync(_context);
        await _categoryService.DeleteAsync(seededCategory);
        
        var exists = await _context.Categories.AnyAsync(c => c.Id == seededCategory.Id);
        Assert.False(exists);
    }

    public void Dispose() => _scope.Dispose();
}