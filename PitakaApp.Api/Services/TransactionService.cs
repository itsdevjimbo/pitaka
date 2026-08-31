using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class TransactionService
{
    private readonly PitakaDbContext _context;

    private readonly UpdateAccountBalance _updateAccountBalance;

    public TransactionService(
        PitakaDbContext context, 
        UpdateAccountBalance updateAccountBalance
    )
    {
        _context = context;
        _updateAccountBalance = updateAccountBalance;
    }
    
    public async Task<(IReadOnlyList<Transaction> Items, int TotalCount)> GetPageForUser(
        User user, TransactionQueryInput query)
    {
        var filtered = _context.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == user.Id);

        if (query.AccountId is int accountId)
        {
            filtered = filtered.Where(t => t.AccountId == accountId || t.TransferToAccountId == accountId);
        }

        if (query.CategoryId is int categoryId)
        {
            filtered = filtered.Where(t => t.CategoryId == categoryId);
        }

        if (query.Type is TransactionType type)
        {
            filtered = filtered.Where(t => t.Type == type);
        }

        if (query.From is DateTime from)
        {
            filtered = filtered.Where(t => t.TransactionDate >= from);
        }

        if (query.To is DateTime to)
        {
            filtered = filtered.Where(t => t.TransactionDate < to);
        }

        var totalCount = await filtered.CountAsync();

        // [Range(1, int.MaxValue)] lets Page be large enough that (Page - 1) * PageSize
        // overflows int; compute the offset in long and clamp so an absurd page number
        // yields an empty page rather than a negative Skip and a database error.
        var skip = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);

        var items = await filtered
            .Include(t => t.Tags)
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<Transaction>> GetAllForAccount(Account account) =>
        await _context.Transactions
            .AsNoTracking()
            .Include(t => t.Tags)
            .Where(t => t.AccountId == account.Id || t.TransferToAccountId == account.Id)
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.Id)
            .ToListAsync();

    public async Task<Transaction> CreateAsync(Account account, CreateTransactionInput input, List<Tag>? tags = null)
    {
        var transaction = new Transaction
        {
            UserId = account.UserId,
            AccountId = account.Id,
            CategoryId = input.CategoryId,
            Type = input.Type,
            Amount = input.Amount,
            TransactionDate = input.TransactionDate?.ToUniversalTime() ?? DateTime.UtcNow,
            Description = input.Description,
            TransferToAccountId = input.TransferToAccountId,
            IsRecurring = input.IsRecurring ?? false,
            RecurringTransactionId = input.RecurringTransactionId
        };

        _context.Transactions.Add(transaction);

        if (tags != null)
        {
            AttachTag(transaction, tags);
        }
        
        await _updateAccountBalance.ApplyTransaction(transaction);
        
        await _context.SaveChangesAsync();

        return transaction;
    }
    public async Task<Transaction?> GetByIdForUser(User user, int id) =>
        await _context.Transactions
            .AsNoTracking()
            .Include(t => t.Tags)
            .Where(t => t.Id == id && t.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Transaction?> GetTrackedByIdAsync(int id) => 
        await _context.Transactions
            .Include(t => t.Tags)
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();

    public async Task<Transaction> UpdateAsync(Transaction transaction, UpdateTransactionInput input, List<Tag>? tags = null)
    {
        transaction.CategoryId = input.CategoryId;
        transaction.Description = input.Description;
        transaction.TransactionDate = input.TransactionDate?.ToUniversalTime() ?? transaction.TransactionDate;
        
        if (tags != null)
        {
            SyncTags(transaction, tags);
        }

        await _context.SaveChangesAsync();
        return transaction;
    }

    public async Task DeleteAsync(Transaction transaction)
    {
        await _updateAccountBalance.ReverseTransaction(transaction);

        var contributions = await _context.GoalContributions
            .Where(gc => gc.TransactionId == transaction.Id)
            .ToListAsync();

        _context.GoalContributions.RemoveRange(contributions);
        _context.Transactions.Remove(transaction);
        
        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsValidTransferTransaction(User user, int? transferToAccountId)
    {
        return await _context.Accounts.AnyAsync(a => a.Id == transferToAccountId && a.UserId == user.Id && a.IsActive);
    }

    private void AttachTag(Transaction transaction, List<Tag> tags)
    {
        foreach (var tag in tags)
        {
            transaction.Tags.Add(tag);
        }
    }

    private void DetachTag(Transaction transaction, List<Tag> tags)
    {
        foreach (var tag in tags)
        {
            transaction.Tags.Remove(tag);
        }
    }

    private void SyncTags(Transaction transaction, List<Tag> tags)
    {
        var tagIds = tags.Select(tag => tag.Id);
        var toRemoveTags = transaction.Tags
            .Where(t => !tagIds.Contains(t.Id))
            .ToList();
            
        if (toRemoveTags.Count > 0)
        {
            DetachTag(transaction, toRemoveTags);
        }

        var tagIdsToSkip = transaction.Tags.Select(tag => tag.Id).ToArray();
        var toAttachTags = tags.Where(t => !tagIdsToSkip.Contains(t.Id)).ToList();
        
        AttachTag(transaction, toAttachTags);
    }
}