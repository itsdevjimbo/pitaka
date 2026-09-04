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

    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        var dueRecurringTransactions = await _getDueRecurringTransactions.GetAsync();
        
        foreach (var recurringTransaction in dueRecurringTransactions)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var freshRecurringTransaction = await _context.RecurringTransactions.Where(rt => rt.Id == recurringTransaction.Id).FirstOrDefaultAsync();

                if (freshRecurringTransaction == null)
                {
                    _logger.LogWarning("Missing recurring transaction: {Id}", recurringTransaction.Id);
                    continue;
                }

                var transactionDate = freshRecurringTransaction.NextRunDate.ToDateTime(TimeOnly.MinValue);
                var transaction = GenerateTransaction.GetTransaction(freshRecurringTransaction, transactionDate);
                
                await _updateAccountBalance.ApplyTransaction(transaction);
                _context.Transactions.Add(transaction);

                var nextRunDate = _getNextRunDate.ExclusiveOfToday(freshRecurringTransaction.StartDate, freshRecurringTransaction.Frequency);

                if (nextRunDate > freshRecurringTransaction.EndDate)
                {
                    freshRecurringTransaction.Status = Enums.RecurringTransactionStatus.Completed;
                }
                else
                {
                    freshRecurringTransaction.NextRunDate = nextRunDate;
                }

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Expected and self-healing: Account.Version lost an optimistic-concurrency
                // race, so the next tick regenerates this schedule. Stays at Warning so a
                // routine lost race doesn't read as an alert.
                _logger.LogWarning(ex, "Recurring transaction {Id} lost a concurrency race; it will be retried next run.", recurringTransaction.Id);
                _context.ChangeTracker.Clear();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any other failure — an FK violation, an unmapped enum, a missing account —
                // must not abandon the rest of the run. Discard the half-applied balance
                // mutation and the added Transaction that are still tracked, then move to the
                // next schedule. A persistent failure now starves only itself, not every
                // schedule ordered behind it. Cancellation is left to propagate.
                _logger.LogError(ex, "Recurring transaction {Id} failed to generate; skipping it for this run.", recurringTransaction.Id);
                _context.ChangeTracker.Clear();
            }
        }

    }
}