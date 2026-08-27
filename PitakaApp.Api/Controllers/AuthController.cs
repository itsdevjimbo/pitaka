namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;

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
        var user = await _loginUser.ExecuteAsync(request.Email, request.Password);
        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = _generateJwtToken.Execute(user);
        var userResponse = new UserResponse(user.Id, user.Name, user.Email);

        return Ok(new LoginResponse(token, userResponse));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await _registerUser.ExecuteAsync(request.Name, request.Email, request.Password);

        if (user == null)
        {
            return Problem(detail: "A user with this email already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(new UserResponse(user.Id, user.Name, user.Email));
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

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);

public record UserResponse(int Id, string Name, string Email);
public record LoginResponse(string Token, UserResponse User);