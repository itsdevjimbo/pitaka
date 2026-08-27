using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class LoginUser
{
    private readonly PitakaDbContext _context;

    public LoginUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(string email, string password)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return null;
        }

        var hasher = new PasswordHasher<User>();
        

        var verificationResult = hasher.VerifyHashedPassword(user, user.PasswordHash, password);


        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await _context.SaveChangesAsync();
        }


        return user;
    }
}