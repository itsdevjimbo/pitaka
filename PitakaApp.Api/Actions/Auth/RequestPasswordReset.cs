using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Actions.Auth;

public class RequestPasswordReset
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly PasswordResetOption _option;

    public RequestPasswordReset(UserManager<User> userManager, IEmailSender emailSender, IOptions<PasswordResetOption> option)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _option = option.Value;
    }

    // Returns nothing at all — not a bool, not a nullable Profile. An unknown email is a
    // silent no-op inside the action, so the controller has no outcome to branch on and
    // the endpoint's indistinguishability cannot rot into a 404.
    public async Task ExecuteAsync(RequestPasswordResetInput input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user is null)
        {
            return;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Identity's DataProtectorTokenProvider output is base64, not base64url — it can
        // contain characters a query string does not carry unescaped.
        var encodedToken = Uri.EscapeDataString(token);

        // Email is never null here — same guarantee as SendEmailConfirmation.ExecuteAsync.
        await _emailSender.SendAsync(user.Email!, "Reset your Pitaka password", ComposeBody(user.Id, encodedToken));
    }

    // Plain text. Says Profile, never "user" or "account", per CONTEXT.md. States that
    // ignoring the message leaves the password unchanged. Carries the configured client
    // reset URL with the Profile id and token appended.
    private string ComposeBody(int userId, string encodedToken) =>
        $"""
        Hi,

        We received a request to reset the password for your Pitaka Profile.

        Choose a new password here:
        {_option.ResetUrl}?userId={userId}&token={encodedToken}

        If you did not ask for this, you can ignore this message — your password
        will not change.

        — Pitaka
        """;
}
