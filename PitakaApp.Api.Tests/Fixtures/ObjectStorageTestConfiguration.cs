namespace PitakaApp.Api.Tests.Fixtures;

internal static class ObjectStorageTestConfiguration
{
    internal static void AddTo(IDictionary<string, string?> settings)
    {
        settings["ObjectStorage:Endpoint"] = "http://localhost:8333";
        settings["ObjectStorage:Region"] = "us-east-1";
        settings["ObjectStorage:BucketName"] = "pitaka-test";
        settings["ObjectStorage:AccessKeyId"] = "test-only-object-storage-key";
        settings["ObjectStorage:SecretAccessKey"] = "test-only-object-storage-secret";
    }
}
