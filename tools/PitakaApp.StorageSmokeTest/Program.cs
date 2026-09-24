using Amazon.S3;
using Amazon.S3.Model;
using PitakaApp.Api.Infra;

const string objectKey = "smoke-test/api-client.txt";
var expectedContent = "Pitaka S3 client storage smoke test"u8.ToArray();
var mode = args.SingleOrDefault();

if (mode is not ("write" or "verify" or "delete"))
{
    throw new ArgumentException("Expected one argument: write, verify, or delete.");
}

var builder = WebApplication.CreateBuilder();
builder.AddObjectStorage();
using var app = builder.Build();
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};
var cancellationToken = cancellationSource.Token;

var s3Client = app.Services.GetRequiredService<IAmazonS3>();
var bucketName = app.Configuration["ObjectStorage:BucketName"]!;

switch (mode)
{
    case "write":
        await using (var content = new MemoryStream(expectedContent))
        {
            await s3Client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    InputStream = content,
                },
                cancellationToken
            );
        }

        await VerifyObjectAsync(
            s3Client,
            bucketName,
            objectKey,
            expectedContent,
            cancellationToken
        );
        Console.WriteLine($"API S3 client wrote and read s3://{bucketName}/{objectKey}.");
        break;

    case "verify":
        await VerifyObjectAsync(
            s3Client,
            bucketName,
            objectKey,
            expectedContent,
            cancellationToken
        );
        Console.WriteLine($"API S3 client read s3://{bucketName}/{objectKey}.");
        break;

    case "delete":
        await s3Client.DeleteObjectAsync(bucketName, objectKey, cancellationToken);
        Console.WriteLine($"API S3 client deleted s3://{bucketName}/{objectKey}.");
        break;
}

static async Task VerifyObjectAsync(
    IAmazonS3 s3Client,
    string bucketName,
    string objectKey,
    byte[] expectedContent,
    CancellationToken cancellationToken
)
{
    using var response = await s3Client.GetObjectAsync(bucketName, objectKey, cancellationToken);
    await using var actualContent = new MemoryStream();
    await response.ResponseStream.CopyToAsync(actualContent, cancellationToken);

    if (!actualContent.ToArray().AsSpan().SequenceEqual(expectedContent))
    {
        throw new InvalidDataException("The stored object does not match the expected content.");
    }
}
