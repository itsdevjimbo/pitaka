using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Data;

namespace PitakaApp.Api.Jobs;

public class GenerateDueRecurringTransactions
{
    private readonly PitakaDbContext _context;

    private readonly GetDueRecurringTransactions _getDueRecurringTransactions;

    private readonly UpdateAccountBalance _updateAccountBalance;

    private readonly GetNextRunDate _getNextRunDate;

    private readonly ILogger<GenerateDueRecurringTransactions> _logger;

    public GenerateDueRecurringTransactions(
        PitakaDbContext context,
        GetDueRecurringTransactions getDueRecurringTransactions,
        UpdateAccountBalance updateAccountBalance,
        GetNextRunDate getNextRunDate,
        ILogger<GenerateDueRecurringTransactions> logger
    )
    {
        _context = context;
        _getDueRecurringTransactions = getDueRecurringTransactions;
        _updateAccountBalance = updateAccountBalance;
        _getNextRunDate = getNextRunDate;
        _logger = logger;
    }


    public async Task GenerateAsync()
    {
        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();
        
        foreach (var recurringTransaction in dueRecurringTransactions)
        {
            try
            {
                var transactionDate = recurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
                var transaction = GenerateTransaction.GetTransaction(recurringTransaction, transactionDate);

                await _updateAccountBalance.ApplyTransaction(transaction);
                _context.Transactions.Add(transaction);

                var nextRunDate = _getNextRunDate.ExclusiveOfToday(recurringTransaction.StartDate, recurringTransaction.Frequency);

                _context.RecurringTransactions.Attach(recurringTransaction);
                
                if (nextRunDate > recurringTransaction.EndDate)
                {
                    recurringTransaction.Status = Enums.RecurringTransactionStatus.Completed;
                }
                else
                {
                    recurringTransaction.NextRunDate = nextRunDate;
                }

                await _context.SaveChangesAsync();   
            } 
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Recurring transaction {Id} failed to generate due to a concurrency conflict.", recurringTransaction.Id);
                _context.ChangeTracker.Clear();
            }
        }

    }
}