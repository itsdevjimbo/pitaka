namespace PitakaApp.Api.Tests.Fixtures;

internal static class TestDatabaseGuard
{
    public static void EnsureDatabaseName(string actualDatabaseName, string expectedDatabaseName)
    {
        if (
            !string.Equals(
                actualDatabaseName,
                expectedDatabaseName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"Test fixture expected database '{expectedDatabaseName}' but was configured for "
                    + $"'{actualDatabaseName}'. Refusing to reset it."
            );
        }
    }
}
