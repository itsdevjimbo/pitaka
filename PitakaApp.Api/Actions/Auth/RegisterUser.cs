using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class RegisterUser
{
    
    private readonly PitakaDbContext _context;

    public RegisterUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(string name, string email, string password)
    {
        var exists = await _context.Users.AnyAsync(u => u.Email == email);
            
        if (exists)
        {
            return null;
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