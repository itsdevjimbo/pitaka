using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using PitakaApp.Api.Tests.Factories;
using PitakaApp.Api.Tests.Fixtures;

namespace PitakaApp.Api.Tests.Actions.Auth;


[Collection("Database collection")]
public class GetCurrentUserTest : IDisposable
{

    private readonly IServiceScope _scope;
    private readonly GetCurrentUser _getCurrentUser;
    private readonly PitakaDbContext _context;
    public GetCurrentUserTest(PitakaWebApplicationFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _getCurrentUser = _scope.ServiceProvider.GetRequiredService<GetCurrentUser>();
        _context = _scope.ServiceProvider.GetRequiredService<PitakaDbContext>();
    }

    [Fact]
    public async Task GetCurrentUser_WithValidUser_ReturnsUser()
    {
        var validClaim = ClaimsPrincipalFactory.ForUser(await UserFactory.CreateAsync(_context));
        var user = _getCurrentUser.ExecuteAsync(validClaim);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task GetCurrentUser_WithInvalidUser_ReturnsNull()
    {
        var invalidClaim = ClaimsPrincipalFactory.WithInvalidUserIdClaim();
        var user = _getCurrentUser.ExecuteAsync(invalidClaim);
        Assert.NotNull(user);
    }

    public void Dispose() => _scope.Dispose();
}