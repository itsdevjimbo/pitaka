namespace PitakaApp.Api.Data;

using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static void Seed(DbContext context)
    {
        AdminSeeder.Seed(context);
        CategorySeeder.Seed(context);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        AdminSeeder.Seed(context);
        CategorySeeder.Seed(context);
        await context.SaveChangesAsync(cancellationToken);
    }
}