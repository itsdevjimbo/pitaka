namespace PitakaApp.Api.Tests.Services;

using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;
using PitakaApp.Api.Services;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

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
        Assert.Equal(17, categories.Count);
    }

    [Fact]
    public async Task GetAll_WithUser_ReturnsCorrectLength()
    {
        var user = await UserFactory.CreateAsync(_context);
        
        await CategoryFactory.CreateAsync(_context, user.Id);

        var categories = await _categoryService.GetAllForUser(user);
        
        Assert.Equal(18, categories.Count);
    }

    [Fact]
    public async Task GetAll_WithCrossUser_ReturnsCorrectLength()
    {
        var userA = await UserFactory.CreateAsync(_context);
        var userB = await UserFactory.CreateAsync(_context);
        
        await CategoryFactory.CreateAsync(_context, userA.Id);
        await CategoryFactory.CreateAsync(_context, userA.Id);

        await CategoryFactory.CreateAsync(_context, userB.Id);

        var categoriesA = await _categoryService.GetAllForUser(userA);
        Assert.Equal(19, categoriesA.Count);
        
        var categoriesB = await _categoryService.GetAllForUser(userB);
        Assert.Equal(18, categoriesB.Count);
    }


    public void Dispose() => _scope.Dispose();
}