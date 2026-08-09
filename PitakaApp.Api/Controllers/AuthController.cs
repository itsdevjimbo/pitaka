namespace PitakaApp.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using PitakaApp.Api.Actions.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly RegisterUser _registerUser;

    public AuthController(RegisterUser registerUser)
    {
        _registerUser = registerUser;
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

public record RegisterRequest(string Name, string Email, string Password);

// Response DTO — deliberately excludes PasswordHash. Never return that, even hashed.
public record UserResponse(int Id, string Name, string Email);