using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class VerifyCategoryExistence
{
    private readonly PitakaDbContext _context;
    public VerifyCategoryExistence(PitakaDbContext context)
    {
        _context = context;
    }

    public async Task<bool> VerifyAsync(User user, int categoryId)
    {
        return await _context.Categories
            .AnyAsync(c => c.Id == categoryId && (c.UserId == user.Id || c.IsDefault));
    }
}