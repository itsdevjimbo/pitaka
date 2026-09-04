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

    public AuthController(
        LoginUser loginUser,
        RegisterUser registerUser,
        GenerateJwtToken generateJwtToken,
        GetCurrentUser getCurrentUser,
        RequestPasswordReset requestPasswordReset,
        ResetPassword resetPassword
    )
    {
        _loginUser = loginUser;
        _registerUser = registerUser;
        _generateJwtToken = generateJwtToken;
        _getCurrentUser = getCurrentUser;
        _requestPasswordReset = requestPasswordReset;
        _resetPassword = resetPassword;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _loginUser.ExecuteAsync(request.ToInput());
        if (user == null)
        {
            return Problem(detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = _generateJwtToken.Execute(user);
        var userResponse = new UserResponse(user.Id, user.Name, user.Email!);

        return Ok(new LoginResponse(token, userResponse));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await _registerUser.ExecuteAsync(request.ToInput());

        if (user == null)
        {
            return Problem(detail: "A user with this email already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var token = _generateJwtToken.Execute(user);
        var userResponse = new UserResponse(user.Id, user.Name, user.Email!);

        // 201 with no Location header — matches AccountsController.Create. There is no
        // canonical GET /users/{id} to point at; GET /api/auth/me is derived from the token.
        return StatusCode(StatusCodes.Status201Created, new LoginResponse(token, userResponse));
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