using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

// The Profile write surface, kept off AuthController deliberately (spec: the
// endpoint-shape decision put the Profile writes here; GET api/auth/me stays where it
// is). Ticket 02 lands the request handler; ticket 03 the confirm handler; cancel
// follows in ticket 04.
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
    private readonly CurrentUserAccessor _currentUserAccessor;

    public ProfileController(
        RequestEmailChange requestEmailChange,
        RedeemEmailChange redeemEmailChange,
        CurrentUserAccessor currentUserAccessor)
    {
        _requestEmailChange = requestEmailChange;
        _redeemEmailChange = redeemEmailChange;
        _currentUserAccessor = currentUserAccessor;
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
            // which register's own string predates.
            case RequestEmailChangeOutcome.EmailTaken:
                return Problem(
                    detail: "A Profile with this email already exists.",
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

            // The address was taken between the request and this click. Its own 409, so
            // the person picks a different address instead of clicking a dead link
            // again (spec story 13).
            case RedeemEmailChangeOutcome.EmailTaken:
                return Problem(
                    detail: "A Profile with this email already exists.",
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
}
