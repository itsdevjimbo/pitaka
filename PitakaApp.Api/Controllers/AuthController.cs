using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Requests;

namespace PitakaApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly LoginUser _loginUser;
    private readonly RegisterUser _registerUser;
    private readonly GenerateJwtToken _generateJwtToken;

    private readonly GetCurrentUser _getCurrentUser;

    public AuthController(
        LoginUser loginUser,
        RegisterUser registerUser,
        GenerateJwtToken generateJwtToken,
        GetCurrentUser getCurrentUser
    )
    {
        _loginUser = loginUser;
        _registerUser = registerUser;
        _generateJwtToken = generateJwtToken;
        _getCurrentUser = getCurrentUser;
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
        var userResponse = new UserResponse(user.Id, user.Name, user.Email);

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
        var userResponse = new UserResponse(user.Id, user.Name, user.Email);

        // 201 with no Location header — matches AccountsController.Create. There is no
        // canonical GET /users/{id} to point at; GET /api/auth/me is derived from the token.
        return StatusCode(StatusCodes.Status201Created, new LoginResponse(token, userResponse));
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
        
        return Ok(new UserResponse(user.Id, user.Name, user.Email));
    }
}

public record UserResponse(int Id, string Name, string Email);
public record LoginResponse(string Token, UserResponse User);