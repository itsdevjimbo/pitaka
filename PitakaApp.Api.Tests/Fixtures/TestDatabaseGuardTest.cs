namespace PitakaApp.Api.Tests.Fixtures;

public class TestDatabaseGuardTest
{
    [Fact]
    public void FixtureReset_DevelopmentDatabase_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestDatabaseGuard.EnsureDatabaseName("pitaka", "pitaka_test")
        );

        Assert.Contains("Refusing to reset it", exception.Message);
    }

    [Fact]
    public void FixtureReset_DedicatedDatabase_IsAccepted()
    {
        var exception = Record.Exception(() =>
            TestDatabaseGuard.EnsureDatabaseName("PITAKA_TEST", "pitaka_test")
        );

        Assert.Null(exception);
    }
}
