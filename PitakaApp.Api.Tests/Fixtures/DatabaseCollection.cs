namespace PitakaApp.Api.Tests.Fixtures;

[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<PitakaWebApplicationFactory>
{
    // No code needed — this class only exists to carry the [CollectionDefinition]
    // attribute, so every [Collection("Database collection")] test class shares one
    // PitakaWebApplicationFactory instance instead of each getting its own.
}
