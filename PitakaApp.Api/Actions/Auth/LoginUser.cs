using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class LoginUser
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;

    public LoginUser(UserManager<User> userManager, SignInManager<User> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<User?> ExecuteAsync(LoginInput input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);

        if (user == null)
        {
            return null;
        }

        // lockoutOnFailure: true — a failed attempt counts toward lockout even though S1
        // keeps the result invisible. Only Succeeded maps to a session here; IsLockedOut
        // and IsNotAllowed both collapse into the same generic failure as a wrong
        // password, exactly as today (see .scratch/auth-identity/spec.md, slice S1). S2
        // branches on them.
        var result = await _signInManager.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return null;
        }

        return user;
    }
}
