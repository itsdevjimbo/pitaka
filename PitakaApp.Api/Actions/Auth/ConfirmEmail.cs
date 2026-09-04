using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class ConfirmEmail
{
    private readonly UserManager<User> _userManager;

    public ConfirmEmail(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    // A bare bool — an unknown id, a bad token and an expired token all collapse here,
    // where they are detected, into the one indistinguishable outcome the controller
    // renders as one 400. Same shape as ResetPassword.ExecuteAsync.
    public async Task<bool> ExecuteAsync(ConfirmEmailInput input)
    {
        var user = await _userManager.FindByIdAsync(input.UserId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await _userManager.ConfirmEmailAsync(user, input.Token);
        return result.Succeeded;
    }
}
