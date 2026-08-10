namespace PitakaApp.Api.Actions.Auth;

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

public class GetCurrentUser
{
    private readonly PitakaDbContext _context;

    public GetCurrentUser(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ExecuteAsync(ClaimsPrincipal principal)
    {
        var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }
}