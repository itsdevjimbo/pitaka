using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Email is `string?` on IdentityUser<int>, but RequireUniqueEmail plus every write
    // path here (RegisterUser, UserFactory) always setting it means a resolved User
    // never actually carries a null one — the `!`s below are that guarantee, not a
    // suppressed bug.

    private readonly LoginUser _loginUser;
    private readonly RegisterUser _registerUser;
    private readonly GenerateJwtToken _generateJwtToken;

    private readonly GetCurrentUser _getCurrentUser;
    private readonly RequestPasswordReset _requestPasswordReset;
    private readonly ResetPassword _resetPassword;
    private readonly ConfirmEmail _confirmEmail;
    private readonly ResendConfirmation _resendConfirmation;

    public AuthController(
        LoginUser loginUser,
        RegisterUser registerUser,
        GenerateJwtToken generateJwtToken,
        GetCurrentUser getCurrentUser,
        RequestPasswordReset requestPasswordReset,
        ResetPassword resetPassword,
        ConfirmEmail confirmEmail,
        ResendConfirmation resendConfirmation
    )
    {
        _loginUser = loginUser;
        _registerUser = registerUser;
        _generateJwtToken = generateJwtToken;
        _getCurrentUser = getCurrentUser;
        _requestPasswordReset = requestPasswordReset;
        _resetPassword = resetPassword;
        _confirmEmail = confirmEmail;
        _resendConfirmation = resendConfirmation;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await _loginUser.ExecuteAsync(request.ToInput());

        switch (result.Outcome)
        {
            case LoginOutcome.Succeeded:
                var token = _generateJwtToken.Execute(result.User!);
                var userResponse = new UserResponse(result.User!.Id, result.User.Name, result.User.Email!);
                return Ok(new LoginResponse(token, userResponse));

            // Unconfirmed email, any password — Identity's confirmed-account gate runs
            // before the password check, so this fires whether or not the password is
            // right. Supersedes the pre-S2 behaviour where this was indistinguishable
            // from a wrong password — see ADR 0012.
            case LoginOutcome.NotConfirmed:
                return Problem(detail: "Confirm your email to sign in.", statusCode: StatusCodes.Status403Forbidden);

            case LoginOutcome.LockedOut:
                return Problem(
                    detail: "Too many failed sign-in attempts. Try again shortly.",
                    statusCode: StatusCodes.Status423Locked);

            case LoginOutcome.InvalidCredentials:
                return Problem(detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

            default:
                throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, "Unhandled login outcome.");
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await _registerUser.ExecuteAsync(request.ToInput());

        if (user == null)
        {
            return Problem(detail: "A user with this email already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var userResponse = new UserResponse(user.Id, user.Name, user.Email!);

        // 201 with the Profile only — no token. A new Profile cannot sign in until it
        // confirms the email RegisterUser just sent (ADR 0012). No Location header —
        // matches AccountsController.Create; there is no canonical GET /users/{id}.
        return StatusCode(StatusCodes.Status201Created, userResponse);
    }

    // Anonymous. Body carries the userId and token RegisterUser/ResendConfirmation put
    // on the confirm link. Unknown id, bad token and expired token all collapse to the
    // same 400 — an onlooker cannot tell which one happened.
    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
    {
        var succeeded = await _confirmEmail.ExecuteAsync(request.ToInput());
        if (!succeeded)
        {
            return Problem(
                detail: "This confirmation link is invalid or has expired.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }

    // Always 202 Accepted with no body, for a known unconfirmed address, a confirmed
    // address and an unknown one alike — same indistinguishability as forgot-password.
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request)
    {
        await _resendConfirmation.ExecuteAsync(request.ToInput());
        return Accepted();
    }

    // Always 202 Accepted with no body, for a known address and an unknown one alike —
    // the response deliberately does not report whether anything was sent. A malformed
    // email still 400s on Email via [ApiController] validation.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        await _requestPasswordReset.ExecuteAsync(request.ToInput());
        return Accepted();
    }

    // Token and new password only. Unknown, expired and already-spent tokens all fail
    // as one 400 ProblemDetails with one non-specific detail. Success is 204 and does
    // not hand back a session — possession of an emailed token is not proof while B5 is
    // out of scope.
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var succeeded = await _resetPassword.ExecuteAsync(request.ToInput());
        if (!succeeded)
        {
            return Problem(
                detail: "This password reset link is invalid or has expired.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _getCurrentUser.ExecuteAsync(User);

        if (user == null)
        {
            return Unauthorized();
        }
        
        return Ok(new UserResponse(user.Id, user.Name, user.Email!));
    }
}

public record UserResponse(int Id, string Name, string Email);
public record LoginResponse(string Token, UserResponse User);