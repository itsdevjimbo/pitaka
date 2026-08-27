using Microsoft.EntityFrameworkCore;

namespace PitakaApp.Api.Data;

public static class DbSeeder
{
    public static void Seed(DbContext context)
    {
        CategorySeeder.Seed(context);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        CategorySeeder.Seed(context);
        await context.SaveChangesAsync(cancellationToken);
    }
}