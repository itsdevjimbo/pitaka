using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Actions.Auth;

// Shared by RegisterUser (the first confirmation email) and ResendConfirmation (a fresh
// one) so the token generation and the email body are written once.
public class SendEmailConfirmation
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly EmailConfirmationOption _option;

    public SendEmailConfirmation(UserManager<User> userManager, IEmailSender emailSender, IOptions<EmailConfirmationOption> option)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _option = option.Value;
    }

    public async Task ExecuteAsync(User user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Identity's DataProtectorTokenProvider output is base64, not base64url — it can
        // contain characters a query string does not carry unescaped.
        var encodedToken = Uri.EscapeDataString(token);

        // Email is never null here — same guarantee as RequestPasswordReset.ExecuteAsync.
        await _emailSender.SendAsync(user.Email!, "Confirm your Pitaka Profile", ComposeBody(user.Id, encodedToken));
    }

    // Plain text. Says Profile, never "user" or "account", per CONTEXT.md. States that
    // ignoring the message leaves the Profile unusable. Carries the configured client
    // confirm URL with the Profile id and token appended.
    private string ComposeBody(int userId, string encodedToken) =>
        $"""
        Hi,

        Welcome to Pitaka. Confirm your Profile to finish signing up:
        {_option.ConfirmUrl}?userId={userId}&token={encodedToken}

        If you ignore this message, your Profile stays unusable — you will not
        be able to sign in until it is confirmed.

        — Pitaka
        """;
}
