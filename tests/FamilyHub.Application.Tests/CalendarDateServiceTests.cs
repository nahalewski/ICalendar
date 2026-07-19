using FamilyHub.Application;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class CalendarDateServiceTests
{
    [Fact]
    public void MonthGridAlwaysHasSixSundayFirstWeeks()
    {
        var grid = CalendarDateService.BuildSundayFirstMonthGrid(2026, 7);
        Assert.Equal(42, grid.Length);
        Assert.Equal(DayOfWeek.Sunday, grid[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, grid[^1].DayOfWeek);
        Assert.Contains(new DateOnly(2026, 7, 19), grid);
    }

    [Fact]
    public void WeekRangeRunsSundayThroughSaturday()
    {
        var (start, end) = CalendarDateService.GetSundayFirstWeek(new DateOnly(2026, 7, 22));
        Assert.Equal(new DateOnly(2026, 7, 19), start);
        Assert.Equal(new DateOnly(2026, 7, 25), end);
    }

    [Fact]
    public void InvalidMonthIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarDateService.BuildSundayFirstMonthGrid(2026, 13));
}
