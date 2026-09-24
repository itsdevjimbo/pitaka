using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Tests.Factories;

public static class StoredFileFactory
{
    public static StoredFile Make(
        string objectKey,
        string mediaType,
        StoredFileState state,
        DateTime createdAt,
        DateTime? nextAttemptAt = null
    ) =>
        new()
        {
            ObjectKey = objectKey,
            MediaType = mediaType,
            State = state,
            CreatedAt = createdAt,
            NextAttemptAt = nextAttemptAt,
        };

    public static async Task<StoredFile> CreateAsync(
        PitakaDbContext context,
        string objectKey,
        string mediaType,
        StoredFileState state,
        DateTime createdAt,
        DateTime? nextAttemptAt = null
    )
    {
        var file = Make(objectKey, mediaType, state, createdAt, nextAttemptAt);
        context.Files.Add(file);
        await context.SaveChangesAsync();
        return file;
    }
}
