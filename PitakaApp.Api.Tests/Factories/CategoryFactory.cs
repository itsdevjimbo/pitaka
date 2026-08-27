using Bogus;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public static class CategoryFactory
{
    public static Category Make(int? userId = null, string? name = null, CategoryType? type = null)
    {
        var faker = new Faker();

        return new Category
        {
            Name = name ?? faker.Person.FullName, // Placeholder dont know what to set
            UserId = userId,
            Type = type ?? CategoryType.Income,
            IsDefault = userId != null ? false : true
        };  
    }

    public static async Task<Category> CreateAsync(PitakaDbContext context, int? userId = null, string? name = null, CategoryType? type = null)
    {
        var category = Make(userId, name, type);

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category;
    }
}
