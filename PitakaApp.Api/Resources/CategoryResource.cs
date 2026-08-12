using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record CategoryResource(int Id, string Name, CategoryType Type, bool IsDefault, int? ParentId)
{
    public static CategoryResource FromModel(Category category) =>
        new(category.Id, category.Name, category.Type, category.IsDefault, category.ParentId);

    public static List<CategoryResource> Collection(IEnumerable<Category> categories) =>
        categories.Select(FromModel).ToList();
} 