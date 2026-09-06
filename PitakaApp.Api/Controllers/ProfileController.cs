using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Filters;
using PitakaApp.Api.Requests;
using PitakaApp.Api.Services;

namespace PitakaApp.Api.Controllers;

// The Profile write surface, kept off AuthController deliberately (spec: the
// endpoint-shape decision put the Profile writes here; GET api/auth/me stays where it
// is). Ticket 02 lands the first handler; confirm and cancel follow in tickets 03 and 04.
[TypeFilter(typeof(ResolveCurrentUserFilter))]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly RequestEmailChange _requestEmailChange;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public ProfileController(RequestEmailChange requestEmailChange, CurrentUserAccessor currentUserAccessor)
    {
        _requestEmailChange = requestEmailChange;
        _currentUserAccessor = currentUserAccessor;
    }

    // Authenticated. Body carries the new address and the current password. Stores the
    // address as a pending email and mails a confirmation link to it; nothing about the
    // live Profile changes and the caller stays signed in (ADR 0014). A request replaces
    // any pending change already in flight. Redeeming the link is ticket 03.
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
}
