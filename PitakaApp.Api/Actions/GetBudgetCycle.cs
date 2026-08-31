using PitakaApp.Api.Enums;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Actions;

// Resolves the calendar window a Budget's spend is measured over. Sibling in shape to
// GetNextRunDate: TimeProvider only, no database. Cycles align to the calendar, not to
// StartDate; the first and last cycles are truncated by StartDate/EndDate and neither is
// pro-rated. See .scratch/budget-cycle-spend/spec.md.
public class GetBudgetCycle
{
    private readonly TimeProvider _timeProvider;

    public GetBudgetCycle(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public (DateOnly Start, DateOnly End) ForBudget(Budget budget)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var endBound = budget.EndDate ?? DateOnly.MaxValue;

        // BudgetRequest.Validate already rejects EndDate < StartDate, so the clamp is safe.
        var anchor = Clamp(today, budget.StartDate, endBound);

        var start = Max(CalendarStart(anchor, budget.Period), budget.StartDate);
        var end = Min(CalendarEnd(anchor, budget.Period), endBound);

        return (start, end);
    }

    private static DateOnly Clamp(DateOnly value, DateOnly min, DateOnly max) =>
        value < min ? min : value > max ? max : value;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly CalendarStart(DateOnly anchor, BudgetPeriod period) => period switch
    {
        BudgetPeriod.Daily => anchor,
        BudgetPeriod.Weekly => anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)),
        BudgetPeriod.Monthly => new DateOnly(anchor.Year, anchor.Month, 1),
        BudgetPeriod.Quarterly => new DateOnly(anchor.Year, (((anchor.Month - 1) / 3) * 3) + 1, 1),
        BudgetPeriod.Yearly => new DateOnly(anchor.Year, 1, 1),
        _ => throw new InvalidOperationException($"Invalid period: {period}")
    };

    private static DateOnly CalendarEnd(DateOnly anchor, BudgetPeriod period) => period switch
    {
        BudgetPeriod.Daily => anchor,
        BudgetPeriod.Weekly => CalendarStart(anchor, period).AddDays(6),
        BudgetPeriod.Monthly => new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)),
        BudgetPeriod.Quarterly => CalendarStart(anchor, period).AddMonths(3).AddDays(-1),
        BudgetPeriod.Yearly => new DateOnly(anchor.Year, 12, 31),
        _ => throw new InvalidOperationException($"Invalid period: {period}")
    };
}
