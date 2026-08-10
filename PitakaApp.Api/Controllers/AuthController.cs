namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly LoginUser _loginUser;
    private readonly RegisterUser _registerUser;

    public AuthController(
        LoginUser loginUser,
        RegisterUser registerUser
    )
    {
        _loginUser = loginUser;
        _registerUser = registerUser;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _loginUser.ExecuteAsync(request.Email, request.Password);
        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        return Ok(new UserResponse(user.Id, user.Name, user.Email));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = await _registerUser.ExecuteAsync(request.Name, request.Email, request.Password);

        if (user == null)
        {
            return Conflict("A user with this email already exists.");
        }

        return Ok(new UserResponse(user.Id, user.Name, user.Email));
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);

public record UserResponse(int Id, string Name, string Email);