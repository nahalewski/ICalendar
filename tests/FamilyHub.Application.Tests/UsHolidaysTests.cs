using FamilyHub.Core;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class UsHolidaysTests
{
    private static Holiday Find(int year, string name) =>
        UsHolidays.ForYear(year).Single(holiday => holiday.Name == name);

    [Theory]
    // Verified against published Easter dates.
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2030, 4, 21)]
    public void ComputesEasterSunday(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), UsHolidays.EasterSunday(year));

    [Fact]
    public void GoodFridayIsTwoDaysBeforeEaster() =>
        Assert.Equal(new DateOnly(2026, 4, 3), Find(2026, "Good Friday").Date);

    [Theory]
    [InlineData(2026, 1, 19)]  // third Monday in January
    [InlineData(2027, 1, 18)]
    public void MartinLutherKingDayIsTheThirdMonday(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), Find(year, "Martin Luther King Jr. Day").Date);

    [Theory]
    [InlineData(2026, 5, 25)]  // last Monday in May
    [InlineData(2027, 5, 31)]
    public void MemorialDayIsTheLastMonday(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), Find(year, "Memorial Day").Date);

    [Theory]
    [InlineData(2026, 11, 26)]  // fourth Thursday in November
    [InlineData(2027, 11, 25)]
    public void ThanksgivingIsTheFourthThursday(int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), Find(year, "Thanksgiving").Date);

    [Fact]
    public void SaturdayHolidayIsObservedTheFridayBefore()
    {
        // July 4, 2026 falls on a Saturday.
        var independenceDay = Find(2026, "Independence Day");
        Assert.Equal(new DateOnly(2026, 7, 3), independenceDay.Date);
        Assert.Equal(new DateOnly(2026, 7, 4), independenceDay.ActualDate);
        Assert.True(independenceDay.IsObservedShift);
        Assert.Equal("Independence Day (observed)", independenceDay.DisplayName);
    }

    [Fact]
    public void SundayHolidayIsObservedTheMondayAfter()
    {
        // November 11, 2029 falls on a Sunday.
        var veteransDay = Find(2029, "Veterans Day");
        Assert.Equal(new DateOnly(2029, 11, 12), veteransDay.Date);
        Assert.Equal(new DateOnly(2029, 11, 11), veteransDay.ActualDate);
    }

    [Fact]
    public void WeekdayHolidayIsNotShifted()
    {
        // December 25, 2026 falls on a Friday.
        var christmas = Find(2026, "Christmas Day");
        Assert.Equal(new DateOnly(2026, 12, 25), christmas.Date);
        Assert.False(christmas.IsObservedShift);
        Assert.Equal("Christmas Day", christmas.DisplayName);
    }

    [Fact]
    public void FloatingMondayHolidaysAreNeverShifted() =>
        Assert.All(UsHolidays.ForYear(2026)
                .Where(holiday => holiday.Name is "Labor Day" or "Columbus Day" or "Presidents' Day"),
            holiday => Assert.False(holiday.IsObservedShift));

    [Fact]
    public void CoversAllElevenFederalHolidays()
    {
        var federal = UsHolidays.ForYear(2026).Where(holiday => holiday.Kind == HolidayKind.Federal).ToArray();
        Assert.Equal(11, federal.Length);
        Assert.Contains(federal, holiday => holiday.Name == "Juneteenth");
    }

    [Fact]
    public void IncludesTheRequestedObservances()
    {
        var names = UsHolidays.ForYear(2026)
            .Where(holiday => holiday.Kind == HolidayKind.Observance)
            .Select(holiday => holiday.Name)
            .ToArray();
        Assert.Contains("Halloween", names);
        Assert.Contains("Mother's Day", names);
        Assert.Contains("Father's Day", names);
        Assert.Contains("Christmas Eve", names);
        Assert.Contains("Easter Sunday", names);
    }

    [Fact]
    public void MothersDayIsTheSecondSundayInMay() =>
        Assert.Equal(new DateOnly(2026, 5, 10), Find(2026, "Mother's Day").Date);

    [Fact]
    public void FathersDayIsTheThirdSundayInJune() =>
        Assert.Equal(new DateOnly(2026, 6, 21), Find(2026, "Father's Day").Date);

    [Fact]
    public void HolidaysAreReturnedInDateOrder()
    {
        var dates = UsHolidays.ForYear(2026).Select(holiday => holiday.Date).ToArray();
        Assert.Equal(dates.OrderBy(date => date).ToArray(), dates);
    }

    [Fact]
    public void RangeQueryStaysWithinBounds()
    {
        var range = UsHolidays.ForRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        Assert.All(range, holiday => Assert.Equal(7, holiday.Date.Month));
        Assert.Contains(range, holiday => holiday.Name == "Independence Day");
    }

    [Fact]
    public void RangeQueryCatchesNewYearObservedInThePreviousYear()
    {
        // January 1, 2028 is a Saturday, so it is observed on December 31, 2027.
        var range = UsHolidays.ForRange(new DateOnly(2027, 12, 1), new DateOnly(2027, 12, 31));
        Assert.Contains(range, holiday => holiday.Name == "New Year's Day" && holiday.Date == new DateOnly(2027, 12, 31));
    }

    [Fact]
    public void RangeQueryReturnsNothingWhenInverted() =>
        Assert.Empty(UsHolidays.ForRange(new DateOnly(2026, 12, 1), new DateOnly(2026, 1, 1)));
}
