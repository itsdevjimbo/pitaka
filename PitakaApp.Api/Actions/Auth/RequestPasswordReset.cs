using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Actions.Auth;

public class RequestPasswordReset
{
    // 256 bits of RandomNumberGenerator output — unguessable, so no slow KDF is needed
    // on top and a plain digest keeps the lookup a single indexed equality.
    private const int TokenByteLength = 32;

    private readonly PitakaDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordResetOption _option;

    public RequestPasswordReset(
        PitakaDbContext context,
        IEmailSender emailSender,
        TimeProvider timeProvider,
        IOptions<PasswordResetOption> option)
    {
        _context = context;
        _emailSender = emailSender;
        _timeProvider = timeProvider;
        _option = option.Value;
    }

    // Returns nothing at all — not a bool, not a nullable Profile. An unknown email is a
    // silent no-op inside the action, so the controller has no outcome to branch on and
    // the endpoint's indistinguishability cannot rot into a 404.
    public async Task ExecuteAsync(RequestPasswordResetInput input)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == input.Email);
        if (user is null)
        {
            return;
        }

        // Base64Url so it survives a query string unescaped.
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteLength));
        var tokenHash = HashToken(token);

        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime + _option.TokenLifetime,
        });
        await _context.SaveChangesAsync();

        await _emailSender.SendAsync(user.Email, "Reset your Pitaka password", ComposeBody(token));
    }

    // Only the hex SHA-256 digest is ever persisted; the plaintext lives in the email
    // and nowhere else. Shared with ResetPassword so a presented token hashes the same.
    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Plain text. Says Profile, never "user" or "account", per CONTEXT.md. States that
    // ignoring the message leaves the password unchanged. Carries the configured client
    // reset URL with the token appended.
    private string ComposeBody(string token) =>
        $"""
        Hi,

        We received a request to reset the password for your Pitaka Profile.

        Choose a new password here:
        {_option.ResetUrl}?token={token}

        If you did not ask for this, you can ignore this message — your password
        will not change.

        — Pitaka
        """;
}
