using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
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
    
    public async Task<List<Transaction>> GetAllForUser(User user) =>
        await _context.Transactions
            .AsNoTracking()
            .Include(t => t.Tags)
            .Where(a => a.UserId == user.Id)
            .ToListAsync();

    public async Task<List<Transaction>> GetAllForAccount(Account account) =>
        await _context.Transactions
            .AsNoTracking()
            .Include(t => t.Tags)
            .Where(a => a.AccountId == account.Id)
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