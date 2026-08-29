using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class LoginUser
{
    private readonly PitakaDbContext _context;

    public LoginUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(LoginInput input)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == input.Email);

        if (user == null)
        {
            return null;
        }

        var hasher = new PasswordHasher<User>();
        

        var verificationResult = hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);


        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            await _context.SaveChangesAsync();
        }


        return user;
    }
}