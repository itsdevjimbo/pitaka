namespace PitakaApp.Api.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Models;
public static class AdminSeeder
{
    public static void Seed(DbContext context)
    {
        if (context.Set<User>().Any())
        {
            return;
        }

        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Name = "Admin Pitaka",
            Email = "admin@pitaka.com",
            PasswordHash = hasher.HashPassword(null!, "adminpitaka")
        };

        context.Set<User>().Add(user);
    }
}