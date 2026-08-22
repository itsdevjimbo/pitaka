using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

public class GetDueRecurringTransactions
{
    private readonly PitakaDbContext _context;
    
    private readonly TimeProvider _timeProvider;
    
    public GetDueRecurringTransactions(PitakaDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<List<RecurringTransaction>> GetAsync() => 
        await _context.RecurringTransactions
                .AsNoTracking()
                .Where(rt => 
                    rt.Status == Enums.RecurringTransactionStatus.Active && rt.Account.IsActive  && 
                    rt.NextRunDate <= DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)
                )
                .ToListAsync();
}