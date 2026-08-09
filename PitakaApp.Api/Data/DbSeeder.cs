namespace PitakaApp.Api.Data;

using Microsoft.EntityFrameworkCore;

public static class DbSeeder
{
    public static void Seed(DbContext context)
    {
        AdminSeeder.Seed(context);
        UserSeeder.Seed(context);
        CategorySeeder.Seed(context);
        AccountSeeder.Seed(context);
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        AdminSeeder.Seed(context);
        UserSeeder.Seed(context);
        CategorySeeder.Seed(context);
        AccountSeeder.Seed(context);
        await context.SaveChangesAsync(cancellationToken);
    }
}