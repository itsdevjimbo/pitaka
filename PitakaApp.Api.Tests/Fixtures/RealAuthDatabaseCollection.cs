namespace PitakaApp.Api.Tests.Fixtures;

[CollectionDefinition("RealAuthDatabase collection")]
public class RealAuthDatabaseCollection : ICollectionFixture<RealAuthWebApplicationFactory>
{
    // Separate collection (not "Database collection") so this factory's
    // EnsureDeletedAsync/MigrateAsync cycle never runs concurrently with
    // PitakaWebApplicationFactory's — they now also target different databases,
    // but keeping them in separate collections too means they can't interleave
    // even if that ever changes.
}
