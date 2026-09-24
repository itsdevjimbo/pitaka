using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Infra;

namespace PitakaApp.Api.Tests.Storage;

public class ObjectStorageIntegrationTest
{
    [Fact]
    [Trait("Category", "StorageIntegration")]
    public async Task RegisteredS3Client_CanWriteReadAndDeleteAnObject()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddObjectStorage();

        await using var app = builder.Build();
        var s3Client = app.Services.GetRequiredService<IAmazonS3>();
        var bucketName = app.Configuration["ObjectStorage:BucketName"]!;
        var objectKey = $"integration-test/{Guid.NewGuid():N}.txt";
        var expectedContent = "Pitaka object storage integration test"u8.ToArray();
        var objectWasCreated = false;

        try
        {
            await using (var content = new MemoryStream(expectedContent))
            {
                await s3Client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = bucketName,
                        Key = objectKey,
                        InputStream = content,
                    }
                );
            }

            objectWasCreated = true;

            using var response = await s3Client.GetObjectAsync(bucketName, objectKey);
            await using var actualContent = new MemoryStream();
            await response.ResponseStream.CopyToAsync(actualContent);

            Assert.Equal(expectedContent, actualContent.ToArray());
        }
        finally
        {
            if (objectWasCreated)
            {
                await s3Client.DeleteObjectAsync(bucketName, objectKey);
            }
        }
    }
}
