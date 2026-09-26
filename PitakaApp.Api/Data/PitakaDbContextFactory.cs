using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PitakaApp.Api.Data;

public sealed class PitakaDbContextFactory : IDesignTimeDbContextFactory<PitakaDbContext>
{
    public PitakaDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Port=3306;Database=pitaka;User=root;Password=design-time-only";
        var options = new DbContextOptionsBuilder<PitakaDbContext>();

        // Bundle generation must not need a live database. The published migration
        // image targets the MySQL 8 version used by the repository's Compose stack.
        PitakaDbContextConfiguration.Configure(
            options,
            configuration,
            connectionString,
            new Version(8, 0, 0)
        );

        return new PitakaDbContext(options.Options);
    }
}
