using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Services;

public class S3FileStorage(IAmazonS3 s3Client, IOptions<ObjectStorageOption> objectStorageOption)
    : IFileStorage
{
    private readonly IAmazonS3 _s3Client = s3Client;
    private readonly string _bucketName = objectStorageOption.Value.BucketName;

    public async Task PutAsync(
        string objectKey,
        byte[] content,
        string mediaType,
        CancellationToken cancellationToken
    )
    {
        using var input = new MemoryStream(content, writable: false);
        await _s3Client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                InputStream = input,
                ContentType = mediaType,
            },
            cancellationToken
        );
    }

    public async Task<byte[]> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        using var response = await _s3Client.GetObjectAsync(
            new GetObjectRequest { BucketName = _bucketName, Key = objectKey },
            cancellationToken
        );
        await using var output = new MemoryStream();
        await response.ResponseStream.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        _s3Client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _bucketName, Key = objectKey },
            cancellationToken
        );
}
