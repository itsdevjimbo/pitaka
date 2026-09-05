using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public enum RegisterOutcome
{
    Succeeded,
    EmailTaken,
    Failed,
}

// User is populated only when Outcome is Succeeded; Errors only when Failed. EmailTaken
// carries no errors — the controller's wording for that path predates the store and
// does not come from it.
public record RegisterResult(RegisterOutcome Outcome, User? User = null, IEnumerable<IdentityError>? Errors = null);

public class RegisterUser
{
    // MySQL error number for a duplicate entry on a unique index.
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly UserManager<User> _userManager;
    private readonly SendEmailConfirmation _sendEmailConfirmation;

    public RegisterUser(UserManager<User> userManager, SendEmailConfirmation sendEmailConfirmation)
    {
        _userManager = userManager;
        _sendEmailConfirmation = sendEmailConfirmation;
    }

    public async Task<RegisterResult> ExecuteAsync(RegisterInput input)
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

            if (!result.Succeeded)
            {
                // Duplicate is the one failure the controller already has wording for.
                // Anything else the store rejects — a validator added after this
                // comment included — comes back as its own errors instead of silently
                // reusing "email already exists".
                var isDuplicate = result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");

                return isDuplicate
                    ? new RegisterResult(RegisterOutcome.EmailTaken)
                    : new RegisterResult(RegisterOutcome.Failed, Errors: result.Errors);
            }
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: DuplicateKeyErrorNumber })
        {
            // The store's own duplicate-email check is the common path; this is the
            // backstop for the instant where two registrations of the same email both
            // pass it and race to insert. Any other DbUpdateException is a real fault:
            // rethrow.
            return new RegisterResult(RegisterOutcome.EmailTaken);
        }

        // S2: RequireConfirmedAccount means this Profile cannot sign in until this link
        // is used. No token is handed back on this path any more — the controller routes
        // to a "check your inbox" screen instead.
        await _sendEmailConfirmation.ExecuteAsync(user);

        return new RegisterResult(RegisterOutcome.Succeeded, user);
    }
}
