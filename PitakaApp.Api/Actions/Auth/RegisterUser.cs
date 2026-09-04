using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class RegisterUser
{
    // MySQL error number for a duplicate entry on a unique index.
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly UserManager<User> _userManager;

    public RegisterUser(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public async Task<User?> ExecuteAsync(RegisterInput input)
    {
        var user = new User
        {
            Name = input.Name,
            Email = input.Email,
            UserName = input.Email,
        };

        try
        {
            var result = await _userManager.CreateAsync(user, input.Password);

            // A duplicate email is the only failure this action's callers act on — the
            // controller's existing null -> 409 branch. Any other IdentityResult failure
            // (password rejected by the store) should not occur: the request-edge
            // [StringLength] on Password already ran first and is stricter than
            // IdentityOptions.Password.
            if (!result.Succeeded)
            {
                return null;
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: DuplicateKeyErrorNumber })
        {
            // The store's own duplicate-email check is the common path; this is the
            // backstop for the instant where two registrations of the same email both
            // pass it and race to insert. Return the same null the check returns — the
            // controller's null -> 409 branch covers both. Any other DbUpdateException
            // is a real fault: rethrow.
            return null;
        }

        return user;
    }
}
