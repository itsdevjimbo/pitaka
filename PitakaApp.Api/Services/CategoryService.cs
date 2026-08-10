using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class CategoryService
{
    private readonly PitakaDbContext _context;

    public CategoryService(PitakaDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Category>> GetAllForUser(User user) =>
        await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsDefault || c.UserId == user.Id)
            .ToListAsync();

    public async Task<List<Category>> GetSystemDefaults() =>
        await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsDefault)
            .ToListAsync();
}