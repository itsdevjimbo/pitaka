using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

// Renames the Profile — the one label the app says back to the person on every screen
// and in every email it sends them.
//
// No password gate, unlike the email change: moving an address moves the recovery path,
// which is an account-takeover step; a name steals nothing and the person who notices
// can reverse it (spec, ADR 0011). Name validity is enforced at the request layer
// (UpdateProfileRequest); this action only persists.
public class ChangeProfileName(UserManager<User> userManager)
{
    private readonly UserManager<User> _userManager = userManager;

    // Returns the stored Profile so the controller renders its response from the row
    // that was actually written, not the AsNoTracking copy off ResolveCurrentUserFilter.
    public async Task<User> ExecuteAsync(User user, ChangeProfileNameInput input)
    {
        // Reload through the manager so the instance we mutate is the one the store
        // tracks — same shape as RequestEmailChange / ResetPassword.
        var managed =
            await _userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException($"Profile {user.Id} vanished mid-request.");

        // Only Name. Email, the UserName mirror, the pending-email columns and every
        // credential field are left exactly as they were.
        managed.Name = input.Name;

        var stored = await _userManager.UpdateAsync(managed);
        if (!stored.Succeeded)
        {
            throw new InvalidOperationException(
                $"Storing the Profile name failed: {string.Join(", ", stored.Errors.Select(e => e.Description))}"
            );
        }

        return managed;
    }
}
