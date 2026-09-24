using System.Collections.Concurrent;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Tests.Fixtures;

public class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();
    private int _putCount;

    public Exception? PutFailure { get; set; }

    public Exception? GetFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public Func<int, string, byte[], Task>? BeforePutAsync { get; set; }

    public ConcurrentQueue<string> DeletedKeys { get; } = new();

    public async Task PutAsync(
        string objectKey,
        byte[] content,
        string mediaType,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PutFailure is { } failure)
        {
            throw failure;
        }

        var putNumber = Interlocked.Increment(ref _putCount);
        if (BeforePutAsync is { } beforePut)
        {
            await beforePut(putNumber, objectKey, content);
        }

        _objects[objectKey] = [.. content];
    }

    public Task<byte[]> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetFailure is { } failure)
        {
            throw failure;
        }

        if (!_objects.TryGetValue(objectKey, out var content))
        {
            throw new KeyNotFoundException($"Object {objectKey} does not exist.");
        }

        return Task.FromResult(content.ToArray());
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeletedKeys.Enqueue(objectKey);
        if (DeleteFailure is { } failure)
        {
            throw failure;
        }

        _objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }

    public void Store(string objectKey, byte[] content) => _objects[objectKey] = [.. content];

    public bool Contains(string objectKey) => _objects.ContainsKey(objectKey);

    public byte[] Read(string objectKey) => [.. _objects[objectKey]];
}
