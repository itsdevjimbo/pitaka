namespace PitakaApp.Api.Actions.Auth;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

public class RegisterUser
{
    
    private readonly PitakaDbContext _context;

    public RegisterUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(string name, string email, string password)
    {
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
        {
            return null;   // signal "already exists" — controller decides what HTTP response that means
        }

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Name = name,
            Email = email,
            PasswordHash = hasher.HashPassword(null!, password),
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }
}