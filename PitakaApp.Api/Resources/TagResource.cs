using PitakaApp.Api.Models;

namespace PitakaApp.Api.Resources;

public record TagResource (int Id, string Name)
{
    
    public static TagResource FromModel(Tag tag) => new (tag.Id, tag.Name);

    public static List<TagResource> Collection(IEnumerable<Tag> tags) =>
        tags.Select(FromModel).ToList();
}