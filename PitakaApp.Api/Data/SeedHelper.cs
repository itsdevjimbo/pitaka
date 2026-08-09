namespace PitakaApp.Api.Data;

using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Models;

public static class SeedHelper
{
    public static User? ExtractAdminUser(DbContext context)
    {
        return ExtractUser(context, "admin@pitaka.com");
    }

    public static User? ExtractTransactionUser(DbContext context)
    {
        return ExtractUser(context, "transaction@pitaka.com");
    }
    
    private static User? ExtractUser(DbContext context, string Email)
    {
        return context.Set<User>().Local.FirstOrDefault(u => u.Email == Email) ?? context.Set<User>().FirstOrDefault(u => u.Email == Email);
    }
}