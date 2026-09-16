using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class GetCurrentUser(PitakaDbContext context)
{
    private readonly PitakaDbContext _context = context;

    public async Task<User?> ExecuteAsync(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(claim, out var userId))
        {
            return null;
        }

        return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
    }
}
