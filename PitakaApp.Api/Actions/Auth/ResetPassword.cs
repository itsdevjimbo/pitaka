using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class ResetPassword
{
    private readonly UserManager<User> _userManager;

    public ResetPassword(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    // A bare bool. Unknown id, bad token and expired token all collapse here, where they
    // are detected, not where they are rendered — a richer result type enumerating them
    // would be an invitation to map each to its own status, which is exactly what must
    // not happen. Same shape as ConfirmEmail.ExecuteAsync.
    public async Task<bool> ExecuteAsync(ResetPasswordInput input)
    {
        var user = await _userManager.FindByIdAsync(input.UserId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await _userManager.ResetPasswordAsync(user, input.Token, input.Password);
        return result.Succeeded;
    }
}
