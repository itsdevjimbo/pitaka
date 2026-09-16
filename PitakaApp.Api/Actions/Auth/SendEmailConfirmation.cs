using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Actions.Auth;

// Shared by RegisterUser (the first confirmation email) and ResendConfirmation (a fresh
// one) so the token generation and the email body are written once.
public class SendEmailConfirmation(
    UserManager<User> userManager,
    IEmailSender emailSender,
    IOptions<EmailConfirmationOption> option
)
{
    private readonly UserManager<User> _userManager = userManager;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly EmailConfirmationOption _option = option.Value;

    public async Task ExecuteAsync(User user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Identity's DataProtectorTokenProvider output is base64, not base64url — it can
        // contain characters a query string does not carry unescaped.
        var encodedToken = Uri.EscapeDataString(token);
        var url = $"{_option.ConfirmUrl}?userId={user.Id}&token={encodedToken}";

        // Email is never null here — same guarantee as RequestPasswordReset.ExecuteAsync.
        await _emailSender.SendAsync(
            user.Email!,
            "Confirm your Pitaka Profile",
            ComposeTextBody(url),
            ComposeHtmlBody(url)
        );
    }

    // Plain text. Says Profile, never "user" or "account", per CONTEXT.md. States that
    // ignoring the message leaves the Profile unusable. Carries the configured client
    // confirm URL with the Profile id and token appended.
    private static string ComposeTextBody(string url) =>
        $"""
            Hi,

            Welcome to Pitaka. Confirm your Profile to finish signing up:
            {url}

            If you ignore this message, your Profile stays unusable — you will not
            be able to sign in until it is confirmed.

            — Pitaka
            """;

    private static string ComposeHtmlBody(string url)
    {
        var encodedUrl = WebUtility.HtmlEncode(url);

        return $"""
            <p>Hi,</p>
            <p>Welcome to Pitaka. Confirm your Profile to finish signing up: <a href="{encodedUrl}">{encodedUrl}</a></p>
            <p>If you ignore this message, your Profile stays unusable — you will not be able to sign in until it is confirmed.</p>
            <p>— Pitaka</p>
            """;
    }
}
