using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class TagService
{
    
    private readonly PitakaDbContext _context;

    public TagService(PitakaDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Tag>> GetAllForUser(User user) =>
        await _context.Tags
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .ToListAsync();

    public async Task<List<Tag>> GetByTagsIdsForUser(User user, int[] tagIds) =>
        await _context.Tags
            .AsNoTracking()
            .Where(t => tagIds.Contains(t.Id) && t.UserId == user.Id)
            .ToListAsync();

    public async Task<Tag?> GetByIdForUser(User user, int id) =>
        await _context.Tags
            .AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Tag?> GetTrackedByIdAsync(int id) =>
        await _context.Tags
            .Where(a => a.Id == id)
            .FirstOrDefaultAsync();
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.Tags
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId));

    public async Task<Tag> CreateAsync(User user, TagInput input)
    {
        var tag = new Tag
        {
            UserId = user.Id,
            Name = input.Name,
        };

        _context.Tags.Add(tag);

        await _context.SaveChangesAsync();
        return tag; 
    }

    public async Task<Tag> UpdateAsync(Tag tag, TagInput input)
    {
        tag.Name = input.Name;
        await _context.SaveChangesAsync();
        return tag;
    }

    public async Task DeleteAsync(Tag tag)
    {
        _context.Tags.Remove(tag);
        await _context.SaveChangesAsync();
    }
}