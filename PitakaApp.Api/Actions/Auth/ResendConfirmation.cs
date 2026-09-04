using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class ResendConfirmation
{
    private readonly UserManager<User> _userManager;
    private readonly SendEmailConfirmation _sendEmailConfirmation;

    public ResendConfirmation(UserManager<User> userManager, SendEmailConfirmation sendEmailConfirmation)
    {
        _userManager = userManager;
        _sendEmailConfirmation = sendEmailConfirmation;
    }

    // Returns nothing at all — same shape as RequestPasswordReset.ExecuteAsync. An
    // unknown address and an already-confirmed one are both silent no-ops here, so the
    // controller has no outcome to branch on and the endpoint's indistinguishability
    // cannot rot into a leak.
    public async Task ExecuteAsync(ResendConfirmationInput input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        await _sendEmailConfirmation.ExecuteAsync(user);
    }
}
