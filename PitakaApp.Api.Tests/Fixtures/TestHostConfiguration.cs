namespace PitakaApp.Api.Tests.Fixtures;

internal static class TestHostConfiguration
{
    internal static Dictionary<string, string?> CreateSettings(
        string connectionString,
        string jwtKey
    ) =>
        new()
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Jwt:Key"] = jwtKey,
            ["Jwt:Issuer"] = "PitakaApp",
            ["Jwt:Audience"] = "PitakaAppUsers",
            ["Jwt:ExpiryMinutes"] = "60",
            ["RecurringTransaction:Enabled"] = "false",
            // Satisfy AddObjectStorage's required options in API-host tests without contacting S3.
            ["ObjectStorage:Endpoint"] = "http://localhost:8333",
            ["ObjectStorage:Region"] = "us-east-1",
            ["ObjectStorage:BucketName"] = "pitaka-test",
            ["ObjectStorage:AccessKeyId"] = "test-only-object-storage-key",
            ["ObjectStorage:SecretAccessKey"] = "test-only-object-storage-secret",
        };
}
