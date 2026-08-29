using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class RegisterUser
{

    // MySQL error number for a duplicate entry on a unique index.
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly PitakaDbContext _context;

    public RegisterUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(RegisterInput input)
    {
        var exists = await _context.Users.AnyAsync(u => u.Email == input.Email);

        if (exists)
        {
            return null;
        }

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Name = input.Name,
            Email = input.Email,
            PasswordHash = hasher.HashPassword(null!, input.Password),
        };

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: DuplicateKeyErrorNumber })
        {
            // The pre-check above is the common path; this is the backstop for the instant
            // where two registrations of the same email both pass it and race to insert.
            // Return the same null the pre-check returns — the controller's null -> 409
            // branch covers both. Any other DbUpdateException is a real fault: rethrow.
            return null;
        }

        return user;
    }
}
