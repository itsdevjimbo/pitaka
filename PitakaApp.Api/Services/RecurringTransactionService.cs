using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class RecurringTransactionService
{
    private readonly PitakaDbContext _context;

    private readonly GetNextRunDate _getNextRunDate;

    public RecurringTransactionService(PitakaDbContext context, GetNextRunDate getNextRunDate)
    {
        _context = context;
        _getNextRunDate = getNextRunDate;
    }
    
    public async Task<List<RecurringTransaction>> GetAllForUser(User user) =>
        await _context.RecurringTransactions
            .AsNoTracking()
            .Where(a => a.UserId == user.Id)
            .ToListAsync();

    public async Task<RecurringTransaction?> GetByIdForUser(User user, int id) =>
        await _context.RecurringTransactions
            .AsNoTracking()
            .Where(a => a.Id == id && a.UserId == user.Id)
            .FirstOrDefaultAsync();

    public async Task<RecurringTransaction?> GetTrackedByIdAsync(int id) =>
        await _context.RecurringTransactions
            .Where(a => a.Id == id)
            .FirstOrDefaultAsync();
    public async Task<bool> NameExistsForUserAsync(int userId, string name, int? excludeId = null) =>
        await _context.RecurringTransactions
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId));

    public async Task<RecurringTransaction> CreateAsync(Account account, CreateRecurringTransactionInput input)
    {
        var recurringTransaction = new RecurringTransaction
        {
            UserId = account.UserId,
            AccountId = account.Id,
            Name = input.Name,
            CategoryId = input.CategoryId,
            Amount = input.Amount,
            Type = input.Type,
            Frequency = input.Frequency,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            NextRunDate = input.StartDate,
            Description = input.Description
        };

        _context.RecurringTransactions.Add(recurringTransaction);

        await _context.SaveChangesAsync();
        return recurringTransaction; 
    }

    public async Task<RecurringTransaction> UpdateAsync(RecurringTransaction recurringTransaction, UpdateRecurringTransactionInput input)
    {
        recurringTransaction.Name = input.Name;
        recurringTransaction.CategoryId = input.CategoryId;
        recurringTransaction.Amount = input.Amount;
        recurringTransaction.EndDate = input.EndDate;
        recurringTransaction.Description = input.Description;

        await _context.SaveChangesAsync();

        return recurringTransaction;
    }

    public async Task<RecurringTransaction> PatchStatusAsync(RecurringTransaction recurringTransaction, RecurringTransactionStatus status)
    {
        if (status != RecurringTransactionStatus.Active)
        {
            recurringTransaction.Status = status;
            await _context.SaveChangesAsync();
            return recurringTransaction;
        }

        var nextRunDate = _getNextRunDate.GetDate(recurringTransaction.StartDate, recurringTransaction.Frequency);

        if (nextRunDate > recurringTransaction.EndDate)
        {
            recurringTransaction.Status = RecurringTransactionStatus.Completed;
        }
        else
        {
            recurringTransaction.NextRunDate = nextRunDate;
            recurringTransaction.Status = status;
        }

        await _context.SaveChangesAsync();
        return recurringTransaction;
    }

    public async Task DeleteAsync(RecurringTransaction recurringTransaction)
    {
        _context.RecurringTransactions.Remove(recurringTransaction);
        await _context.SaveChangesAsync();
    }
}