namespace PitakaApp.Api.Tests.Fixtures;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    private const string TestConnectionString =
        "Server=localhost;Port=3306;Database=pitaka_test;User=root;Password=root;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
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
