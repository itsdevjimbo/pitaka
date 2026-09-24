using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    LoginUser loginUser,
    RegisterUser registerUser,
    GenerateJwtToken generateJwtToken,
    RequestPasswordReset requestPasswordReset,
    ResetPassword resetPassword,
    ConfirmEmail confirmEmail,
    ResendConfirmation resendConfirmation
) : ControllerBase
{
    // Email is `string?` on IdentityUser<int>, but RequireUniqueEmail plus every write
    // path here (RegisterUser, UserFactory) always setting it means a resolved User
    // never actually carries a null one — the `!`s below are that guarantee, not a
    // suppressed bug.

    private readonly LoginUser _loginUser = loginUser;
    private readonly RegisterUser _registerUser = registerUser;
    private readonly GenerateJwtToken _generateJwtToken = generateJwtToken;

    private readonly RequestPasswordReset _requestPasswordReset = requestPasswordReset;
    private readonly ResetPassword _resetPassword = resetPassword;
    private readonly ConfirmEmail _confirmEmail = confirmEmail;
    private readonly ResendConfirmation _resendConfirmation = resendConfirmation;

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await _loginUser.ExecuteAsync(request.ToInput());

        switch (result.Outcome)
        {
            case LoginOutcome.Succeeded:
                var token = _generateJwtToken.Execute(result.User!);
                var profileResponse = new ProfileResponse(
                    result.User!.Id,
                    result.User.Name,
                    result.User.Email!,
                    HasPicture: result.User.HasPicture
                );
                return Ok(new LoginResponse(token, profileResponse));

            // Unconfirmed email, any password — Identity's confirmed-account gate runs
            // before the password check, so this fires whether or not the password is
            // right. Supersedes the pre-S2 behaviour where this was indistinguishable
            // from a wrong password — see ADR 0012.
            case LoginOutcome.NotConfirmed:
                return Problem(
                    detail: "Confirm your email to sign in.",
                    statusCode: StatusCodes.Status403Forbidden
                );

            case LoginOutcome.LockedOut:
                return Problem(
                    detail: "Too many failed sign-in attempts. Try again shortly.",
                    statusCode: StatusCodes.Status423Locked
                );

            case LoginOutcome.InvalidCredentials:
                return Problem(
                    detail: "Invalid email or password.",
                    statusCode: StatusCodes.Status401Unauthorized
                );

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result.Outcome),
                    result.Outcome,
                    "Unhandled login outcome."
                );
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await _registerUser.ExecuteAsync(request.ToInput());

        switch (result.Outcome)
        {
            case RegisterOutcome.Succeeded:
                var profileResponse = new ProfileResponse(
                    result.User!.Id,
                    result.User.Name,
                    result.User.Email!,
                    HasPicture: result.User.HasPicture
                );

                // 201 with the Profile only — no token. A new Profile cannot sign in until it
                // confirms the email RegisterUser just sent (ADR 0012). No Location header —
                // matches AccountsController.Create; there is no canonical GET /users/{id}.
                return StatusCode(StatusCodes.Status201Created, profileResponse);

            case RegisterOutcome.EmailTaken:
                return Problem(
                    detail: "A user with this email already exists.",
                    statusCode: StatusCodes.Status409Conflict
                );

            case RegisterOutcome.Failed:
                foreach (var error in result.Errors!)
                {
                    ModelState.AddModelError(RegisterErrorField(error.Code), error.Description);
                }
                return ValidationProblem(ModelState);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result.Outcome),
                    result.Outcome,
                    "Unhandled register outcome."
                );
        }
    }

    // The store's IdentityError carries a Code, not a field name. UserName mirrors Email
    // and never surfaces on its own, so a UserName/Email code is reported against Email;
    // anything unrecognised falls back to the request as a whole rather than guessing.
    private static string RegisterErrorField(string code) =>
        code switch
        {
            _ when code.Contains("Password") => nameof(RegisterRequest.Password),
            _ when code.Contains("UserName") || code.Contains("Email") => nameof(
                RegisterRequest.Email
            ),
            _ => string.Empty,
        };

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
                statusCode: StatusCodes.Status400BadRequest
            );
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
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        return NoContent();
    }
}

// The signed-in Profile, read at GET api/profile (ProfileController) and embedded in
// LoginResponse. PendingEmail is nullable and only ever populated by the Profile read —
// login leaves it null. Client counterpart: pitaka-web shows a "pending change"
// indicator when set.
public record ProfileResponse(
    int Id,
    string Name,
    string Email,
    string? PendingEmail = null,
    bool HasPicture = false
);

public record LoginResponse(string Token, ProfileResponse User);
