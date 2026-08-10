namespace PitakaApp.Api.Tests.Factories;

using System.Security.Claims;
using PitakaApp.Api.Models;

public static class ClaimsPrincipalFactory
{
    public static ClaimsPrincipal ForUser(User user)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal WithInvalidUserIdClaim() =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-number") },
            "TestAuthType"));

    public static ClaimsPrincipal WithNoClaims() => new(new ClaimsIdentity());
}