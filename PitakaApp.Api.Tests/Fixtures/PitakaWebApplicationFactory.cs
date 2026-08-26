namespace PitakaApp.Api.Tests.Fixtures;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;

// WebApplicationFactory<T> already implements IAsyncDisposable.DisposeAsync() (returns
// ValueTask). Xunit's IAsyncLifetime also declares a DisposeAsync() (returns Task) with
// the same name and parameters, so it's implemented explicitly below — that keeps it out
// of the class's public surface and avoids colliding with the base class's version.
public class PitakaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string TestConnectionString =
        Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
        ?? "Server=localhost;Port=3306;Database=pitaka_test;User=root;Password=root;";

    // Not a real secret — signs tokens for throwaway test runs only, nothing it signs is
    // ever trusted outside the test process. Lets the test suite (and CI) start without
    // needing the developer's local User Secrets file, which a CI runner never has.
    private const string TestJwtKey = "test-only-jwt-signing-key-not-for-real-use-0000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
                ["Jwt:Key"] = TestJwtKey,
                ["RecurringTransaction:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options => { });
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
