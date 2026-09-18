using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

// Clears a pending change (ADR 0014). Once the pending columns are empty the link that
// was mailed no longer redeems — RedeemEmailChange reads the stored pending address and
// finds nothing. Idempotent: cancelling with nothing pending is a success, not an error.
public class CancelEmailChange(UserManager<User> userManager)
{
    private readonly UserManager<User> _userManager = userManager;

    public async Task ExecuteAsync(User user)
    {
        // Nothing stored — including an expired value that is already treated as absent
        // everywhere. Skip the write so a no-op cancel does not churn UpdatedAt.
        if (user.PendingEmail is null && user.PendingEmailExpiresAt is null)
        {
            return;
        }

        // Reload through the manager so the mutated instance is the one the store tracks:
        // the `user` off ResolveCurrentUserFilter is AsNoTracking. Same shape as
        // RequestEmailChange.
        var managed =
            await _userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException($"Profile {user.Id} vanished mid-request.");
        managed.PendingEmail = null;
        managed.PendingEmailExpiresAt = null;

        var stored = await _userManager.UpdateAsync(managed);
        if (!stored.Succeeded)
        {
            throw new InvalidOperationException(
                $"Clearing the pending email failed: {string.Join(", ", stored.Errors.Select(e => e.Description))}"
            );
        }
    }
}
