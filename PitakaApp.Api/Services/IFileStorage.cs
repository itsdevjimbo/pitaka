namespace PitakaApp.Api.Services;

public interface IFileStorage
{
    Task PutAsync(
        string objectKey,
        byte[] content,
        string mediaType,
        CancellationToken cancellationToken
    );

    Task<byte[]> GetAsync(string objectKey, CancellationToken cancellationToken);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
