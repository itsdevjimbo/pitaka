using Microsoft.AspNetCore.Identity;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    NotConfirmed,
    LockedOut,
}

// User is populated only when Outcome is Succeeded. A richer result is right here,
// unlike ResetPassword's bare bool — S2 needs these three failures to render as three
// different status codes, so collapsing them would have to be undone at the controller.
public record LoginResult(LoginOutcome Outcome, User? User = null);

public class LoginUser
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;

    public LoginUser(UserManager<User> userManager, SignInManager<User> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<LoginResult> ExecuteAsync(LoginInput input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);

        if (user == null)
        {
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        // lockoutOnFailure: true — a failed attempt counts toward lockout. Succeeded,
        // IsLockedOut and IsNotAllowed (the confirmed-account gate) are mutually
        // exclusive outcomes of this one call; a wrong email or wrong password that
        // hits neither still collapses to the same generic InvalidCredentials below.
        var result = await _signInManager.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return new LoginResult(LoginOutcome.Succeeded, user);
        }

        if (result.IsNotAllowed)
        {
            return new LoginResult(LoginOutcome.NotConfirmed);
        }

        if (result.IsLockedOut)
        {
            return new LoginResult(LoginOutcome.LockedOut);
        }

        return new LoginResult(LoginOutcome.InvalidCredentials);
    }
}
