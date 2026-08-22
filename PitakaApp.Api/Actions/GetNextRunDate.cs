using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Actions;

public class GetNextRunDate
{
    private readonly TimeProvider _timeProvider;

    public GetNextRunDate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateOnly InclusiveOfToday(DateOnly startDate, Frequency frequency) =>
        GetOccurrenceOnOrAfter(startDate, frequency, includeToday: true);

    public DateOnly ExclusiveOfToday(DateOnly startDate, Frequency frequency) =>
        GetOccurrenceOnOrAfter(startDate, frequency, includeToday: false);

    private DateOnly GetOccurrenceOnOrAfter(DateOnly startDate, Frequency frequency, bool includeToday)
    {
        DateOnly now = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var occurrences = Math.Max(NumberOfOccurrences(startDate, now, frequency), 0);
        var candidate = AddOccurrences(startDate, frequency, occurrences);

        var needsAdvance = includeToday ? candidate < now : candidate <= now;
        if (needsAdvance)
        {
            candidate = AddOccurrences(startDate, frequency, occurrences + 1);
        }

        return candidate;
    }

    private DateOnly AddOccurrences(DateOnly startDate, Frequency frequency, int occurrences) => frequency switch
    {
        Frequency.Daily => startDate.AddDays(occurrences),
        Frequency.Weekly => startDate.AddDays(occurrences * 7),
        Frequency.Monthly => startDate.AddMonths(occurrences),
        Frequency.Yearly => startDate.AddYears(occurrences),
        _ => throw new InvalidOperationException($"Invalid frequency: {frequency}")
    };

    private int NumberOfOccurrences(DateOnly startDate, DateOnly referenceDate, Frequency frequency) => frequency switch
    {
        Frequency.Daily => referenceDate.DayNumber - startDate.DayNumber,
        Frequency.Weekly => (referenceDate.DayNumber - startDate.DayNumber) / 7,
        Frequency.Monthly => GetFullMonthsBetween(startDate, referenceDate),
        Frequency.Yearly => GetFullYearsBetween(startDate, referenceDate),
        _ => 0
    };

    private int GetFullMonthsBetween(DateOnly start, DateOnly end)
    {
        int months = ((end.Year - start.Year) * 12) + end.Month - start.Month;
        if (end.Day < start.Day) months--;
        return months;
    }

    private int GetFullYearsBetween(DateOnly start, DateOnly end)
    {
        int year = end.Year - start.Year;
        if (end.Month > start.Month) return year;
        if (end.Month < start.Month) return --year;
        return end.Day >= start.Day ? year : --year;
    }
}