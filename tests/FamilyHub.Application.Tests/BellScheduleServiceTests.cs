using FamilyHub.Application;
using FamilyHub.Core;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class BellScheduleServiceTests
{
    // Mirrors the published ALA high school regular day.
    private static readonly BellSchedule HighSchool = new(SchoolLevel.High, "ALA High", "2024-25",
    [
        new DailyBellSchedule("Regular", [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [
            new ScheduleBlock("Block 1", new TimeOnly(8, 10), new TimeOnly(9, 40)),
            new ScheduleBlock("Block 2", new TimeOnly(9, 45), new TimeOnly(11, 15)),
            new ScheduleBlock("Lunch", new TimeOnly(11, 20), new TimeOnly(12, 0), BlockKind.Lunch),
            new ScheduleBlock("Block 3", new TimeOnly(12, 5), new TimeOnly(13, 35)),
            new ScheduleBlock("Block 4", new TimeOnly(13, 40), new TimeOnly(15, 10))
        ]),
        new DailyBellSchedule("Wednesday", [DayOfWeek.Wednesday],
        [
            new ScheduleBlock("ALA Teacher PD", new TimeOnly(7, 30), new TimeOnly(9, 0), BlockKind.StaffOnly),
            new ScheduleBlock("Block 1", new TimeOnly(9, 10), new TimeOnly(10, 10))
        ])
    ]);

    /// <summary>Local wall-clock time, so the assertions hold in any machine timezone.</summary>
    private static DateTimeOffset At(int day, int hour, int minute) =>
        new(new DateTime(2026, 7, day, hour, minute, 0, DateTimeKind.Local));

    // Monday 20 July 2026 and Wednesday 22 July 2026.
    private static DateTimeOffset Monday(int hour, int minute) => At(20, hour, minute);
    private static DateTimeOffset Wednesday(int hour, int minute) => At(22, hour, minute);

    [Fact]
    public void BeforeTheFirstBell()
    {
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(7, 30));
        Assert.Equal(BlockDayState.BeforeSchool, progress.State);
        Assert.Equal("Block 1", progress.Next?.Name);
        Assert.Equal("Starts 8:10 AM", progress.Headline);
    }

    [Fact]
    public void InsideABlockReportsProgressAndTimeLeft()
    {
        // 8:55 is 45 minutes into a 90-minute Block 1 — exactly halfway.
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(8, 55));
        Assert.Equal(BlockDayState.InBlock, progress.State);
        Assert.Equal("Block 1", progress.Current?.Name);
        Assert.Equal(0.5, progress.FractionThroughBlock, 3);
        Assert.Equal(TimeSpan.FromMinutes(45), progress.RemainingInBlock);
        Assert.Contains("45 min left", progress.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockBoundaryBelongsToTheNextBlockNotTheOneEnding()
    {
        // Exactly 9:45 is the start of Block 2, and Block 1 ended at 9:40.
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(9, 45));
        Assert.Equal(BlockDayState.InBlock, progress.State);
        Assert.Equal("Block 2", progress.Current?.Name);
        Assert.Equal(0, progress.FractionThroughBlock);
    }

    [Fact]
    public void PassingPeriodIsBetweenBlocks()
    {
        // 9:42 sits in the gap between Block 1 (ends 9:40) and Block 2 (starts 9:45).
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(9, 42));
        Assert.Equal(BlockDayState.BetweenBlocks, progress.State);
        Assert.Null(progress.Current);
        Assert.Equal("Block 2", progress.Next?.Name);
        Assert.Equal("Passing → Block 2", progress.Headline);
    }

    [Fact]
    public void LunchCountsAsPartOfTheDay()
    {
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(11, 40));
        Assert.Equal(BlockDayState.InBlock, progress.State);
        Assert.Equal("Lunch", progress.Current?.Name);
    }

    [Fact]
    public void AfterTheLastBell()
    {
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(15, 10));
        Assert.Equal(BlockDayState.AfterSchool, progress.State);
        Assert.Equal(1, progress.FractionThroughDay);
        Assert.Equal("Day complete", progress.Headline);
    }

    [Fact]
    public void DayProgressRunsFromFirstBellToLast()
    {
        Assert.Equal(0, BellScheduleService.Evaluate(HighSchool, Monday(8, 10)).FractionThroughDay, 3);
        // 11:40 is 210 of the 420 minutes between 8:10 and 15:10.
        Assert.Equal(0.5, BellScheduleService.Evaluate(HighSchool, Monday(11, 40)).FractionThroughDay, 3);
    }

    [Fact]
    public void WednesdayUsesTheLateStartSchedule()
    {
        // Students are not in school during 7:30 teacher PD, so 8:30 is still "before school".
        var early = BellScheduleService.Evaluate(HighSchool, Wednesday(8, 30));
        Assert.Equal(BlockDayState.BeforeSchool, early.State);
        Assert.Equal("Block 1", early.Next?.Name);

        var later = BellScheduleService.Evaluate(HighSchool, Wednesday(9, 30));
        Assert.Equal(BlockDayState.InBlock, later.State);
    }

    [Fact]
    public void StaffOnlyBlocksAreExcludedFromTheStudentDay()
    {
        var wednesday = HighSchool.For(DayOfWeek.Wednesday)!;
        Assert.Equal(2, wednesday.Blocks.Count);
        Assert.Single(wednesday.StudentBlocks);
        Assert.Equal(new TimeOnly(9, 10), wednesday.FirstBell);
    }

    [Fact]
    public void WeekendHasNoSchedule()
    {
        // Saturday 25 July 2026.
        var progress = BellScheduleService.Evaluate(HighSchool, At(25, 10, 0));
        Assert.Equal(BlockDayState.NoSchoolToday, progress.State);
    }

    [Fact]
    public void CalendarClosureOverridesTheBellSchedule()
    {
        // Mid-morning on a school day, but the school calendar says it is a break.
        var progress = BellScheduleService.Evaluate(HighSchool, Monday(10, 0), "Thanksgiving Break");
        Assert.Equal(BlockDayState.NoSchoolToday, progress.State);
        Assert.Equal("Thanksgiving Break", progress.Headline);
    }

    [Fact]
    public void MissingScheduleIsReportedRatherThanCrashing()
    {
        // Elliana's elementary level has no published schedule.
        var progress = BellScheduleService.Evaluate(null, Monday(10, 0));
        Assert.Equal(BlockDayState.NoSchoolToday, progress.State);
        Assert.Equal("No schedule configured", progress.Headline);
    }
}
