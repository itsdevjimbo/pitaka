using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Inputs;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Services;

public class RecurringTransactionService(PitakaDbContext context, GetNextRunDate getNextRunDate)
{
    private readonly PitakaDbContext _context = context;

    private readonly GetNextRunDate _getNextRunDate = getNextRunDate;

    private IQueryable<RecurringTransactionRead> WithGeneratedTransactionCount(
        IQueryable<RecurringTransaction> recurringTransactions
    ) =>
        recurringTransactions.Select(rt => new RecurringTransactionRead(
            rt,
            _context.Transactions.Count(t => t.RecurringTransactionId == rt.Id)
        ));

    public async Task<List<RecurringTransactionRead>> GetAllForUser(User user) =>
        await WithGeneratedTransactionCount(
                _context.RecurringTransactions.AsNoTracking().Where(rt => rt.UserId == user.Id)
            )
            .ToListAsync();

    public async Task<RecurringTransactionRead?> GetByIdForUser(User user, int id) =>
        await WithGeneratedTransactionCount(
                _context
                    .RecurringTransactions.AsNoTracking()
                    .Where(rt => rt.Id == id && rt.UserId == user.Id)
            )
            .FirstOrDefaultAsync();

    public async Task<RecurringTransaction?> GetTrackedByIdAsync(int id) =>
        await _context.RecurringTransactions.Where(a => a.Id == id).FirstOrDefaultAsync();

    public async Task<bool> NameExistsForUserAsync(
        int userId,
        string name,
        int? excludeId = null
    ) =>
        await _context
            .RecurringTransactions.AsNoTracking()
            .AnyAsync(a =>
                a.UserId == userId && a.Name == name && (excludeId == null || a.Id != excludeId)
            );

    public async Task<RecurringTransaction> CreateAsync(
        Account account,
        CreateRecurringTransactionInput input
    )
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
            Description = input.Description,
        };

        _context.RecurringTransactions.Add(recurringTransaction);

        await _context.SaveChangesAsync();
        return recurringTransaction;
    }

    public async Task<RecurringTransaction> UpdateAsync(
        RecurringTransaction recurringTransaction,
        UpdateRecurringTransactionInput input
    )
    {
        recurringTransaction.Name = input.Name;
        recurringTransaction.CategoryId = input.CategoryId;
        recurringTransaction.Amount = input.Amount;
        recurringTransaction.EndDate = input.EndDate;
        recurringTransaction.Description = input.Description;

        await _context.SaveChangesAsync();

        return recurringTransaction;
    }

    public async Task<RecurringTransaction> PatchStatusAsync(
        RecurringTransaction recurringTransaction,
        RecurringTransactionStatus status
    )
    {
        if (status != RecurringTransactionStatus.Active)
        {
            recurringTransaction.Status = status;
            await _context.SaveChangesAsync();
            return recurringTransaction;
        }

        var nextRunDate = _getNextRunDate.InclusiveOfToday(
            recurringTransaction.StartDate,
            recurringTransaction.Frequency
        );

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

    public async Task<bool> TryDeleteUnusedAsync(int recurringTransactionId) =>
        await _context
            .RecurringTransactions.Where(rt =>
                rt.Id == recurringTransactionId && !rt.HasGeneratedTransactions
            )
            .ExecuteDeleteAsync() == 1;

    public async Task<int> GetGeneratedTransactionCountAsync(int recurringTransactionId) =>
        await _context
            .Transactions.AsNoTracking()
            .CountAsync(t => t.RecurringTransactionId == recurringTransactionId);
}
