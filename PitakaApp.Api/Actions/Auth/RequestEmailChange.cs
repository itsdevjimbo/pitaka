using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Actions.Auth;

public enum RequestEmailChangeOutcome
{
    Succeeded,
    IncorrectPassword,
    UnchangedAddress,
    EmailTaken,
}

// The first half of the change-email flow (ADR 0014): hold the new address as a pending
// email and mail a confirmation link to it. Redeeming that link is ticket 03; the
// courtesy notice to the old address is ticket 07.
public class RequestEmailChange
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _timeProvider;
    private readonly EmailChangeOption _option;

    public RequestEmailChange(
        UserManager<User> userManager,
        IEmailSender emailSender,
        TimeProvider timeProvider,
        IOptions<EmailChangeOption> option)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _timeProvider = timeProvider;
        _option = option.Value;
    }

    // A bare outcome enum, no wrapping record: unlike LoginResult / RegisterResult there
    // is no payload to carry back. The controller renders three of the four as distinct
    // status codes. Nothing is stored or sent unless the outcome is Succeeded.
    public async Task<RequestEmailChangeOutcome> ExecuteAsync(User user, RequestEmailChangeInput input)
    {
        // The password is the only thing in this flow that proves identity — the
        // confirmation link only proves control of the new address, which in the case
        // this guards against is the attacker's own (ADR 0014). CheckPasswordAsync, not
        // the sign-in manager: a wrong password here must not count toward sign-in
        // lockout.
        if (!await _userManager.CheckPasswordAsync(user, input.CurrentPassword))
        {
            return RequestEmailChangeOutcome.IncorrectPassword;
        }

        // A FindByEmailAsync hit on the new address is either the caller's own row — the
        // address is already theirs, there is nothing to change — or another Profile's,
        // in which case answer now rather than leave them waiting on mail that will never
        // be sent. The unique email index is the backstop for the address being claimed
        // between here and redemption; that race surfaces as a 409 in ticket 03.
        var holder = await _userManager.FindByEmailAsync(input.NewEmail);
        if (holder is not null)
        {
            return holder.Id == user.Id
                ? RequestEmailChangeOutcome.UnchangedAddress
                : RequestEmailChangeOutcome.EmailTaken;
        }

        // Reload through the manager so the instance we mutate is the one the store
        // tracks: the `user` off ResolveCurrentUserFilter is AsNoTracking, and
        // UpdateAsync's own uniqueness re-check would otherwise track a second copy of
        // the same row and collide. Same shape as ResetPassword / ConfirmEmail.
        var managed = await _userManager.FindByIdAsync(user.Id.ToString());
        if (managed is null)
        {
            throw new InvalidOperationException($"Profile {user.Id} vanished mid-request.");
        }

        // Overwrites any pending change already in flight, in place. The live Email, the
        // UserName mirror and EmailConfirmed are all left untouched — the caller stays
        // signed in and their current address keeps working.
        managed.PendingEmail = input.NewEmail;
        managed.PendingEmailExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.Add(_option.TokenLifespan);

        var stored = await _userManager.UpdateAsync(managed);
        if (!stored.Succeeded)
        {
            // The invariant is "nothing sent unless it was stored". A failed persist here
            // must not fall through to sending the confirmation link.
            throw new InvalidOperationException(
                $"Storing the pending email failed: {string.Join(", ", stored.Errors.Select(e => e.Description))}");
        }

        var token = await _userManager.GenerateChangeEmailTokenAsync(managed, input.NewEmail);

        // Identity's DataProtectorTokenProvider output is base64, not base64url — same
        // escaping as SendEmailConfirmation.
        var encodedToken = Uri.EscapeDataString(token);

        await _emailSender.SendAsync(
            input.NewEmail,
            "Confirm your new Pitaka Profile email",
            ComposeBody(managed.Id, encodedToken));

        return RequestEmailChangeOutcome.Succeeded;
    }

    // Plain text. Says Profile, never "user" or "account", per CONTEXT.md. Carries the
    // configured client confirm-email-change URL with the Profile id and token appended,
    // same shape as SendEmailConfirmation. States that ignoring it leaves the address
    // unchanged.
    private string ComposeBody(int userId, string encodedToken) =>
        $"""
        Hi,

        We received a request to move your Pitaka Profile to this email address.
        Confirm it to finish the change:
        {_option.ConfirmUrl}?userId={userId}&token={encodedToken}

        If you ignore this message, your Profile keeps its current address — nothing
        changes until this link is used.

        — Pitaka
        """;
}
