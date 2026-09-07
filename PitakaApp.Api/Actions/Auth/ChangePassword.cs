using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public enum ChangePasswordOutcome
{
    Succeeded,
    IncorrectPassword,
}

// Replaces a signed-in Profile's password with the current one supplied as proof. A
// maintenance operation, not a recovery one: the caller holds the current password and
// stays signed in throughout (spec stories 15–21).
//
// A bare outcome enum, no wrapping record: like RequestEmailChange there is no payload
// to carry back — the endpoint answers 204. The controller renders IncorrectPassword as
// the 401 the email change already uses, so both password-gated forms on one client
// screen speak identically.
public class ChangePassword
{
    private readonly UserManager<User> _userManager;

    public ChangePassword(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ChangePasswordOutcome> ExecuteAsync(User user, ChangePasswordInput input)
    {
        // The current password is the only thing proving identity here. CheckPasswordAsync,
        // not the sign-in manager: a signed-in person fumbling their own password must
        // not count toward sign-in lockout (spec, matching RequestEmailChange). Checked
        // on the untracked `user` first, before any reload — a wrong password costs no
        // round-trip, same order as RequestEmailChange.
        if (!await _userManager.CheckPasswordAsync(user, input.OldPassword))
        {
            return ChangePasswordOutcome.IncorrectPassword;
        }

        // Reload through the manager so the instance we mutate is the one the store
        // tracks — the `user` off ResolveCurrentUserFilter is AsNoTracking. Same shape
        // as RequestEmailChange / ChangeProfileName.
        var managed = await _userManager.FindByIdAsync(user.Id.ToString());
        if (managed is null)
        {
            throw new InvalidOperationException($"Profile {user.Id} vanished mid-request.");
        }

        // ChangePasswordAsync rotates the security stamp, which invalidates outstanding
        // confirmation and reset links but not issued JWTs — the caller's session keeps
        // working (ADR 0011, "a live JWT outlives a credential change"). The new
        // password's validity is already guaranteed by ChangePasswordRequest's
        // annotation and the old one is checked just above, so a non-success result here
        // is a store failure, not a rejected input.
        var result = await _userManager.ChangePasswordAsync(managed, input.OldPassword, input.NewPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Changing the Profile password failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        return ChangePasswordOutcome.Succeeded;
    }
}
