using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

// Everything a person can read or change about their own Profile lives here, at
// api/profile: the read (moved from GET api/auth/me, which is removed) and the
// email-change flow, with the name and password writes to follow.
//
// ResolveCurrentUserFilter is applied per-action rather than at class level (as
// CategoriesController does) so the anonymous confirm endpoint — reached from a link in
// a mailbox, not a session — is not short-circuited by it.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly RequestEmailChange _requestEmailChange;
    private readonly RedeemEmailChange _redeemEmailChange;
    private readonly CancelEmailChange _cancelEmailChange;
    private readonly CurrentUserAccessor _currentUserAccessor;
    private readonly TimeProvider _timeProvider;

    public ProfileController(
        RequestEmailChange requestEmailChange,
        RedeemEmailChange redeemEmailChange,
        CancelEmailChange cancelEmailChange,
        CurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider)
    {
        _requestEmailChange = requestEmailChange;
        _redeemEmailChange = redeemEmailChange;
        _cancelEmailChange = cancelEmailChange;
        _currentUserAccessor = currentUserAccessor;
        _timeProvider = timeProvider;
    }

    // Authenticated. The signed-in Profile: id, name, live email, and any email change
    // still pending. Moved verbatim from GET api/auth/me (spec ticket 07). The pending
    // address is surfaced so a person can see which address a change in flight is going
    // to (ADR 0014, spec story 2); PendingEmailAsOf returns null once the pending window
    // has passed, so an expired change is absent rather than shown as still live.
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpGet]
    public IActionResult GetProfile()
    {
        var user = _currentUserAccessor.User!;
        var pendingEmail = user.PendingEmailAsOf(_timeProvider.GetUtcNow().UtcDateTime);

        return Ok(new ProfileResponse(user.Id, user.Name, user.Email!, pendingEmail));
    }

    // Authenticated. Body carries the new address and the current password. Stores the
    // address as a pending email and mails a confirmation link to it; nothing about the
    // live Profile changes and the caller stays signed in (ADR 0014). A request replaces
    // any pending change already in flight. Redeeming the link is ticket 03.
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpPost("email-change")]
    public async Task<IActionResult> RequestEmailChange(RequestEmailChangeRequest request)
    {
        var user = _currentUserAccessor.User!;
        var outcome = await _requestEmailChange.ExecuteAsync(user, request.ToInput());

        switch (outcome)
        {
            case RequestEmailChangeOutcome.Succeeded:
                return NoContent();

            // POST /register's 409 for an address already held; worded per CONTEXT.md,
            // which register's own string predates. Names the remedy — pick another
            // address — since the caller chose this one and can choose again (ticket 06).
            case RequestEmailChangeOutcome.EmailTaken:
                return Problem(
                    detail: "A Profile with this email already exists. Choose a different address.",
                    statusCode: StatusCodes.Status409Conflict);

            // The address submitted is already this Profile's — there is nothing to
            // change, so there is nothing to confirm.
            case RequestEmailChangeOutcome.UnchangedAddress:
                return Problem(
                    detail: "That is already the email address on this Profile.",
                    statusCode: StatusCodes.Status400BadRequest);

            // The current password re-authenticates the caller for this one step; a
            // wrong one fails the way a wrong password fails at sign-in.
            case RequestEmailChangeOutcome.IncorrectPassword:
                return Problem(
                    detail: "Your current password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(outcome), outcome, "Unhandled email-change outcome.");
        }
    }

    // Anonymous, like auth/confirm-email — the person redeems this from a link in their
    // new mailbox, which is the whole point of the proof. Body carries the Profile id
    // and the token, the shape confirm-email uses. Redeeming moves the Profile to its
    // pending address in one transaction (ADR 0014); the new address signs in
    // afterwards and the old one stops.
    [AllowAnonymous]
    [HttpPost("email-change/confirm")]
    public async Task<IActionResult> ConfirmEmailChange(RedeemEmailChangeRequest request)
    {
        var outcome = await _redeemEmailChange.ExecuteAsync(request.ToInput());

        switch (outcome)
        {
            case RedeemEmailChangeOutcome.Succeeded:
                return NoContent();

            // The address was claimed by another Profile between the request and this
            // click. Its own 409, and its own wording: it names what happened, so the
            // person requests the change again with a different address rather than
            // retrying a link that can never work now (spec story 13, ticket 06).
            case RedeemEmailChangeOutcome.EmailTaken:
                return Problem(
                    detail: "This address was taken by another Profile after you requested the change. "
                        + "Request the change again with a different address.",
                    statusCode: StatusCodes.Status409Conflict);

            // Unknown Profile, bad token, expired token, address no longer pending — one
            // non-specific 400 for all of them, the way reset-password collapses its
            // failures so Identity's own messages do not leak.
            case RedeemEmailChangeOutcome.Invalid:
                return Problem(
                    detail: "This email change link is invalid or has expired.",
                    statusCode: StatusCodes.Status400BadRequest);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(outcome), outcome, "Unhandled email-change confirm outcome.");
        }
    }

    // Authenticated. Clears any pending change on the caller's own Profile — there is no
    // target id, so a session can only cancel its own (ADR 0014). Afterwards the pending
    // address is gone from GET api/profile and the link that was mailed no longer
    // redeems. Idempotent: cancelling with nothing pending still answers 204.
    [TypeFilter(typeof(ResolveCurrentUserFilter))]
    [HttpPost("email-change/cancel")]
    public async Task<IActionResult> CancelEmailChange()
    {
        await _cancelEmailChange.ExecuteAsync(_currentUserAccessor.User!);
        return NoContent();
    }
}
