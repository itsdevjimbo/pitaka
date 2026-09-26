using Microsoft.EntityFrameworkCore;

namespace PitakaApp.Api.Data;

public static class PitakaDbContextConfiguration
{
    public static void Configure(
        DbContextOptionsBuilder options,
        IConfiguration configuration,
        string? connectionStringOverride = null,
        Version? defaultServerVersion = null
    )
    {
        var connectionString =
            connectionStringOverride ?? configuration.GetConnectionString("DefaultConnection");
        var configuredVersion = configuration["Database:ServerVersion"];
        var serverVersion =
            !string.IsNullOrWhiteSpace(configuredVersion)
                ? new MySqlServerVersion(Version.Parse(configuredVersion))
            : defaultServerVersion is not null ? new MySqlServerVersion(defaultServerVersion)
            : ServerVersion.AutoDetect(connectionString);

        options
            .UseMySql(connectionString, serverVersion)
            .UseSnakeCaseNamingConvention()
            .UseSeeding((context, _) => DbSeeder.Seed(context))
            .UseAsyncSeeding(
                async (context, _, cancellationToken) =>
                    await DbSeeder.SeedAsync(context, cancellationToken)
            );
    }
}
