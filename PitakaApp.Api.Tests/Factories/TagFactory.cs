using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public class TagFactory
{
    public static Tag Make(int userId, string? name = null)
    {
        return new Tag
        {
            UserId = userId,
            Name = name ?? "Test tag",
        };
    }

    public static async Task<Tag> CreateAsync(PitakaDbContext context, int userId, string? name = null)
    {
        var tag = Make(userId, name);
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag;
    }
}