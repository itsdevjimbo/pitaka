namespace PitakaApp.Api.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Models;
public static class UserSeeder
{
    public static void Seed(DbContext context)
    {
        var user = SeedHelper.ExtractTransactionUser(context);

        if (user != null)
        {
            return;
        }

        var hasher = new PasswordHasher<User>();

        user = new User
        {
            Name = "Trasaction Pitaka",
            Email = "transaction@pitaka.com",
            PasswordHash = hasher.HashPassword(null!, "pitakadev")
        };

        context.Set<User>().Add(user);
    }
}