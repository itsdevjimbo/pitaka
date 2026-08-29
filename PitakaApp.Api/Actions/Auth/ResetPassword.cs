using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions.Auth;

public class ResetPassword
{
    private readonly PitakaDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ResetPassword(PitakaDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    // A bare bool. Unknown, expired and already-spent are collapsed here, where they are
    // detected, not where they are rendered — a richer result type enumerating them would
    // be an invitation to map each to its own status, which is exactly what must not
    // happen.
    public async Task<bool> ExecuteAsync(ResetPasswordInput input)
    {
        var tokenHash = RequestPasswordReset.HashToken(input.Token);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var presented = await _context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (presented is null || presented.UsedAt is not null || presented.ExpiresAt <= now)
        {
            return false;
        }

        var hasher = new PasswordHasher<User>();
        presented.User.PasswordHash = hasher.HashPassword(presented.User, input.Password);

        // Spend the presented token and every other outstanding one for this Profile, so
        // requesting three resets and using one leaves zero live links. The presented
        // token is the same tracked instance, so it is spent by this loop too. The new
        // hash and every UsedAt land in one SaveChangesAsync.
        var outstanding = await _context.PasswordResetTokens
            .Where(t => t.UserId == presented.UserId && t.UsedAt == null)
            .ToListAsync();

        foreach (var token in outstanding)
        {
            token.UsedAt = now;
        }

        await _context.SaveChangesAsync();
        return true;
    }
}
