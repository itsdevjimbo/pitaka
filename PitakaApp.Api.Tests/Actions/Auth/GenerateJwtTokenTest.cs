namespace PitakaApp.Api.Tests.Actions.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Bogus;
using Microsoft.Extensions.Configuration;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Models;

public class GenerateJwtTokenTest
{
    private readonly Faker _faker = new();
    private readonly GenerateJwtToken _generateJwtToken;

    public GenerateJwtTokenTest()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-at-least-32-characters-long",
                ["Jwt:Issuer"] = "PitakaApp",
                ["Jwt:Audience"] = "PitakaAppUsers",
                ["Jwt:ExpiryMinutes"] = "60",
            })
            .Build();

        _generateJwtToken = new GenerateJwtToken(configuration);
    }

    [Fact]
    public void Execute_ReturnsTokenWithCorrectClaims()
    {
        var user = new User
        {
            Id = _faker.Random.Int(1, 1000),
            Name = _faker.Person.FullName,
            Email = _faker.Internet.Email(),
            PasswordHash = "irrelevant-for-this-test",
        };

        var token = _generateJwtToken.Execute(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(user.Email, jwt.Claims.First(c => c.Type == ClaimTypes.Email).Value);
        Assert.Equal("PitakaApp", jwt.Issuer);
    }
}