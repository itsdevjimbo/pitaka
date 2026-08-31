using Microsoft.Extensions.Time.Testing;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Enums;
using PitakaApp.Api.Tests.Factories;

namespace PitakaApp.Api.Tests.Actions;

public class GetBudgetCycleTest
{
    private static (DateOnly Start, DateOnly End) Resolve(
        DateTime utcNow,
        BudgetPeriod period,
        DateOnly startDate,
        DateOnly? endDate = null)
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(utcNow);

        var budget = BudgetFactory.Make(userId: 1, period: period, startDate: startDate, endDate: endDate);

        return new GetBudgetCycle(clock).ForBudget(budget);
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_IsTheDayItself()
    {
        var (start, end) = Resolve(Utc(2026, 08, 15), BudgetPeriod.Daily, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 08, 15), start);
        Assert.Equal(new DateOnly(2026, 08, 15), end);
    }

    [Fact]
    public void Weekly_RunsMondayThroughSunday()
    {
        // 2026-08-15 is a Saturday.
        var (start, end) = Resolve(Utc(2026, 08, 15), BudgetPeriod.Weekly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 08, 10), start);
        Assert.Equal(new DateOnly(2026, 08, 16), end);
    }

    [Fact]
    public void Weekly_TodayOnMonday_StartsThatDay()
    {
        // 2026-08-17 is a Monday.
        var (start, end) = Resolve(Utc(2026, 08, 17), BudgetPeriod.Weekly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 08, 17), start);
        Assert.Equal(new DateOnly(2026, 08, 23), end);
    }

    [Fact]
    public void Weekly_TodayOnSunday_EndsThatDay()
    {
        // 2026-08-16 is a Sunday.
        var (start, end) = Resolve(Utc(2026, 08, 16), BudgetPeriod.Weekly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 08, 10), start);
        Assert.Equal(new DateOnly(2026, 08, 16), end);
    }

    [Fact]
    public void Monthly_28DayMonth()
    {
        var (start, end) = Resolve(Utc(2026, 02, 15), BudgetPeriod.Monthly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 02, 01), start);
        Assert.Equal(new DateOnly(2026, 02, 28), end);
    }

    [Fact]
    public void Monthly_31DayMonth()
    {
        var (start, end) = Resolve(Utc(2026, 08, 15), BudgetPeriod.Monthly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 08, 01), start);
        Assert.Equal(new DateOnly(2026, 08, 31), end);
    }

    [Fact]
    public void Quarterly_FirstQuarter()
    {
        var (start, end) = Resolve(Utc(2026, 02, 15), BudgetPeriod.Quarterly, new DateOnly(2026, 01, 01));

        Assert.Equal(new DateOnly(2026, 01, 01), start);
        Assert.Equal(new DateOnly(2026, 03, 31), end);
    }

    [Theory]
    [InlineData(2026, 04, 01, 2026, 04, 01, 2026, 06, 30)] // first day of Q2
    [InlineData(2026, 06, 30, 2026, 04, 01, 2026, 06, 30)] // last day of Q2
    [InlineData(2026, 07, 01, 2026, 07, 01, 2026, 09, 30)] // first day of Q3
    [InlineData(2026, 11, 20, 2026, 10, 01, 2026, 12, 31)] // inside Q4
    public void Quarterly_AlignsToCalendarQuarter(
        int y, int m, int d,
        int startY, int startM, int startD,
        int endY, int endM, int endD)
    {
        var (start, end) = Resolve(Utc(y, m, d), BudgetPeriod.Quarterly, new DateOnly(2025, 01, 01));

        Assert.Equal(new DateOnly(startY, startM, startD), start);
        Assert.Equal(new DateOnly(endY, endM, endD), end);
    }

    [Fact]
    public void Yearly_RunsJanuaryFirstThroughDecemberThirtyFirst()
    {
        var (start, end) = Resolve(Utc(2026, 05, 15), BudgetPeriod.Yearly, new DateOnly(2020, 03, 09));

        Assert.Equal(new DateOnly(2026, 01, 01), start);
        Assert.Equal(new DateOnly(2026, 12, 31), end);
    }

    [Fact]
    public void FirstCycle_IsTruncatedByStartDate()
    {
        // A Monthly Budget starting the 17th reports the 17th-31st, not the 1st-31st.
        var (start, end) = Resolve(Utc(2026, 08, 20), BudgetPeriod.Monthly, new DateOnly(2026, 08, 17));

        Assert.Equal(new DateOnly(2026, 08, 17), start);
        Assert.Equal(new DateOnly(2026, 08, 31), end);
    }

    [Fact]
    public void LastCycle_IsTruncatedByEndDate()
    {
        var (start, end) = Resolve(
            Utc(2026, 08, 10), BudgetPeriod.Monthly, new DateOnly(2026, 08, 01), new DateOnly(2026, 08, 20));

        Assert.Equal(new DateOnly(2026, 08, 01), start);
        Assert.Equal(new DateOnly(2026, 08, 20), end);
    }

    [Fact]
    public void StartDateInFuture_DescribesTheFirstCycle()
    {
        var (start, end) = Resolve(Utc(2026, 08, 15), BudgetPeriod.Monthly, new DateOnly(2026, 09, 10));

        Assert.Equal(new DateOnly(2026, 09, 10), start);
        Assert.Equal(new DateOnly(2026, 09, 30), end);
    }

    [Fact]
    public void EndDateInPast_DescribesTheFinalCycle()
    {
        var (start, end) = Resolve(
            Utc(2026, 08, 15), BudgetPeriod.Monthly, new DateOnly(2026, 01, 01), new DateOnly(2026, 06, 20));

        Assert.Equal(new DateOnly(2026, 06, 01), start);
        Assert.Equal(new DateOnly(2026, 06, 20), end);
    }

    [Fact]
    public void StartDateAndEndDateInSameCycle_ProduceOneShortWindow()
    {
        var (start, end) = Resolve(
            Utc(2026, 08, 15), BudgetPeriod.Monthly, new DateOnly(2026, 08, 10), new DateOnly(2026, 08, 20));

        Assert.Equal(new DateOnly(2026, 08, 10), start);
        Assert.Equal(new DateOnly(2026, 08, 20), end);
    }
}
