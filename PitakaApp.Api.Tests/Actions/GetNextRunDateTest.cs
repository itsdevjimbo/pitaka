using Microsoft.Extensions.Time.Testing;
using PitakaApp.Api.Actions;
using PitakaApp.Api.Enums;

namespace PitakaApp.Api.Tests.Actions;

public class GetNextRunDateTest
{
    [Fact]
    public void GetNextRunDate_Daily()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 08, 28, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(new DateOnly(2026, 08, 28), service.GetDate(startDate, Frequency.Daily));
    }

    [Fact]
    public void GetNextRunDate_WithTheSameDate_Daily()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 08, 21, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(new DateOnly(2026, 08, 21), service.GetDate(startDate, Frequency.Daily));
    }
    
    [Fact]
    public void GetNextRunDate_WithStartDateToFuture_Daily()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 24);
        var mockUtcTime = new DateTime(2026, 08, 21, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(new DateOnly(2026, 08, 24), service.GetDate(startDate, Frequency.Daily));
    }

    [Fact]
    public void GetNextRunDate_Weekly()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 08, 29, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(startDate.AddDays(14), service.GetDate(startDate, Frequency.Weekly));
    }

    [Fact]
    public void GetNextRunDate_WithTheSameDate_Weekly()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 08, 21, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(startDate, service.GetDate(startDate, Frequency.Weekly));
    }

    [Fact]
    public void GetNextRunDate_Monthly()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 08, 28, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(startDate.AddMonths(1), service.GetDate(startDate, Frequency.Monthly));
    }

    [Fact]
    public void GetNextRunDate_WithTheSameDate_Monthly()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2026, 09, 21, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(new DateOnly(2026, 09, 21), service.GetDate(startDate, Frequency.Monthly));
    }

    [Fact]
    public void GetNextRunDate_Monthly_Jan31_Feb28()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 01, 31);
        var mockUtcTime = new DateTime(2026, 02, 03, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(new DateOnly(2026, 02, 28), service.GetDate(startDate, Frequency.Monthly));
    }

    [Fact]
    public void GetNextRunDate_Yearly()
    {
        var fakeClock = new FakeTimeProvider();

        var startDate = new DateOnly(2026, 08, 21);
        var mockUtcTime = new DateTime(2027, 05, 28, 12, 0 ,0, DateTimeKind.Utc);
        fakeClock.SetUtcNow(mockUtcTime);

        var service = new GetNextRunDate(fakeClock);
        Assert.Equal(startDate.AddYears(1), service.GetDate(startDate, Frequency.Yearly));
    }
}