using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Infra;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Tests.Storage;

public class ObjectStorageIntegrationTest
{
    [Fact]
    [Trait("Category", "StorageIntegration")]
    public async Task RegisteredProfilePictureStorage_CanWriteReadAndDeleteAnObject()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddObjectStorage();

        await using var app = builder.Build();
        var storage = app.Services.GetRequiredService<IProfilePictureStorage>();
        var objectKey = $"integration-test/{Guid.NewGuid():N}.txt";
        var expectedContent = "Pitaka object storage integration test"u8.ToArray();
        var objectWasCreated = false;

        try
        {
            await storage.PutAsync(
                objectKey,
                expectedContent,
                "text/plain",
                CancellationToken.None
            );
            objectWasCreated = true;

            var actualContent = await storage.GetAsync(objectKey, CancellationToken.None);
            Assert.Equal(expectedContent, actualContent);
        }
        finally
        {
            if (objectWasCreated)
            {
                await storage.DeleteAsync(objectKey, CancellationToken.None);
            }
        }
    }
}
