using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class TagService(
    PitakaDbContext context,
    CheckUserScopedNameExists checkUserScopedNameExists
)
{
    private readonly PitakaDbContext _context = context;
    private readonly CheckUserScopedNameExists _checkUserScopedNameExists =
        checkUserScopedNameExists;

    public async Task<List<Tag>> GetAllForUser(User user) =>
        await _context.Tags.AsNoTracking().Where(a => a.UserId == user.Id).ToListAsync();

    public async Task<List<Tag>> GetByTagsIdsForUserAsync(
        User user,
        int[] tagIds,
        CancellationToken cancellationToken = default
    ) =>
        await _context
            .Tags.Where(t => tagIds.Contains(t.Id) && t.UserId == user.Id)
            .ToListAsync(cancellationToken);

    public async Task<Tag?> GetByIdForUser(User user, int id) =>
        await _context
            .Tags.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Tag?> GetTrackedByIdForUserAsync(
        int userId,
        int id,
        CancellationToken cancellationToken
    ) =>
        await _context
            .Tags.Where(tag => tag.Id == id && tag.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> NameExistsForUserAsync(
        int userId,
        string name,
        int? excludeId = null,
        CancellationToken cancellationToken = default
    ) =>
        _checkUserScopedNameExists.ExecuteAsync(
            _context.Tags,
            userId,
            name,
            tag => tag.UserId,
            tag => tag.Name,
            tag => tag.Id,
            excludeId,
            cancellationToken
        );

    public async Task<Tag> CreateAsync(
        User user,
        TagInput input,
        CancellationToken cancellationToken = default
    )
    {
        var tag = new Tag { UserId = user.Id, Name = input.Name };

        _context.Tags.Add(tag);

        await _context.SaveChangesAsync(cancellationToken);
        return tag;
    }

    public async Task<Tag> UpdateAsync(
        Tag tag,
        TagInput input,
        CancellationToken cancellationToken = default
    )
    {
        tag.Name = input.Name;
        await _context.SaveChangesAsync(cancellationToken);
        return tag;
    }

    public async Task DeleteAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        _context.Tags.Remove(tag);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
