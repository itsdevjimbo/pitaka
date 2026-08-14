namespace PitakaApp.Api.Tests.Fixtures;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;

// Deliberately does NOT register TestAuthHandler, unlike PitakaWebApplicationFactory —
// [Authorize] here resolves to the real JwtBearerHandler configured in Program.cs, so
// tests using this factory exercise actual token signature/issuer/audience/expiry
// validation instead of the fake header-based auth used everywhere else.
//
// Uses its own database (pitaka_test_realauth), not the shared pitaka_test, specifically
// to avoid racing PitakaWebApplicationFactory's EnsureDeletedAsync/MigrateAsync cycle —
// xUnit runs different collections in parallel, and two factories independently
// dropping/recreating the same database is exactly the bug fixed a few sessions back.
public class RealAuthWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestConnectionString =
        "Server=localhost;Port=3306;Database=pitaka_test_realauth;User=root;Password=root;";

    // Not a real secret — same reasoning as PitakaWebApplicationFactory's TestJwtKey. This
    // factory validates real signatures, so the key must be set to *something* valid; the
    // actual value only needs to be self-consistent within this factory's own host.
    private const string TestJwtKey = "test-only-jwt-signing-key-not-for-real-use-0000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
                ["Jwt:Key"] = TestJwtKey,
            });
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();

        if (await context.Database.CanConnectAsync())
        {
            await context.Database.EnsureDeletedAsync();
        }

        await context.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
