using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record CategoryResource(int Id, string Name, string Type, bool IsDefault)
{
    public static CategoryResource FromModel(Category category) =>
        new(category.Id, category.Name, category.Type.ToString(), category.IsDefault);

    public static List<CategoryResource> Collection(IEnumerable<Category> categories) =>
        categories.Select(FromModel).ToList();
} 