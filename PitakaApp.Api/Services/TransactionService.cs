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

    private readonly CategoryService _categoryService;

    public TransactionService(
        PitakaDbContext context, 
        UpdateAccountBalance updateAccountBalance,
        CategoryService categoryService
    )
    {
        _context = context;
        _updateAccountBalance = updateAccountBalance;
        _categoryService = categoryService;
    }
    
    public async Task<List<Transaction>> GetAllForUser(User user) =>
        await _context.Transactions
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .ToListAsync();

    public async Task<Transaction> CreateAsync(Account account, CreateTransactionInput input)
    {
        var transaction = new Transaction
        {
            UserId = account.UserId,
            AccountId = account.Id,
            CategoryId = input.CategoryId,
            Type = input.Type,
            Amount = input.Amount,
            TransactionDate = input.TransactionDate ?? DateTime.Now,
            Description = input.Description,
            TransferToAccountId = input.TransferToAccountId,
            IsRecurring = input.IsRecurring ?? false,
            RecurringTransactionId = input.RecurringTransactionId
        };

        _context.Transactions.Add(transaction);
        await _updateAccountBalance.ApplyTransaction(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }
    public async Task<Transaction?> GetByIdForUser(User user, int id) =>
        await _context.Transactions
            .AsNoTracking()
            .Where(t => t.Id == id && t.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<Transaction?> GetTrackedByIdAsync(int id) => 
        await _context.Transactions
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();

    public async Task<Transaction> UpdateAsync(Transaction transaction, UpdateTransactionInput input)
    {
        transaction.CategoryId = input.CategoryId;
        transaction.Description = input.Description;
        transaction.TransactionDate = input.TransactionDate ?? transaction.TransactionDate;
        
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

    public async Task<bool> VerifyCategoryExistence(User user, int? categoryId)
    {
        if (categoryId is not int id)
        {
            return true;
        }
        
        var category = await _categoryService.GetByIdForUser(user, id);
        return category != null;
    }

    public async Task<bool> IsValidTransferTransaction(User user, int? transferToAccountId)
    {
        return await _context.Accounts.AnyAsync(a => a.Id == transferToAccountId && a.UserId == user.Id && a.IsActive);
    }
}