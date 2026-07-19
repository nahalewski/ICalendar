using FamilyHub.Core;
using FamilyHub.Infrastructure;
using Xunit;

namespace FamilyHub.Application.Tests;

/// <summary>
/// Guards the school-calendars.json that actually ships, so a hand edit cannot quietly break the
/// file or move a date the family depends on.
/// </summary>
public sealed class ShippedSchoolCalendarTests
{
    private static readonly JsonSchoolCalendarProvider Provider = new(ShippedFilePath());

    private static string ShippedFilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyHub.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "FamilyHub.App", "school-calendars.json");
    }

    private static SchoolCalendar Harnett() =>
        Provider.LoadAll().Single(calendar => calendar.ShortName == "HCS");

    [Fact]
    public void ShippedFileParses() => Assert.NotEmpty(Provider.LoadAll());

    [Fact]
    public void HarnettMatchesTheOfficialCalendar()
    {
        var hcs = Harnett();
        Assert.Equal("2023-2024", hcs.SchoolYear);
        Assert.Equal(new DateOnly(2023, 8, 28), hcs.Entries.Single(e => e.Kind == SchoolDayKind.FirstDay).Date);
        Assert.Equal(new DateOnly(2024, 5, 29), hcs.Entries.Single(e => e.Kind == SchoolDayKind.LastDay).Date);
    }

    [Fact]
    public void SpringBreakCoversTheWholeWeek()
    {
        // April 1-5, 2024 is shaded Teacher Vacation across the full week.
        var hcs = Harnett();
        foreach (var day in Enumerable.Range(1, 5).Select(d => new DateOnly(2024, 4, d)))
            Assert.Contains(hcs.On(day), entry => entry.Title == "Teacher vacation");
        Assert.DoesNotContain(hcs.On(new DateOnly(2024, 4, 8)), entry => entry.Title == "Teacher vacation");
    }

    [Fact]
    public void ThanksgivingAndChristmasAreMarkedNoSchool()
    {
        var hcs = Harnett();
        Assert.Contains(hcs.On(new DateOnly(2023, 11, 23)), entry => entry.Kind == SchoolDayKind.NoSchool);
        Assert.Contains(hcs.On(new DateOnly(2023, 12, 26)), entry => entry.Kind == SchoolDayKind.NoSchool);
    }

    [Fact]
    public void EveryEntryFallsInsideItsSchoolYear()
    {
        foreach (var calendar in Provider.LoadAll())
        {
            var startYear = int.Parse(calendar.SchoolYear.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture);
            foreach (var entry in calendar.Entries)
            {
                Assert.InRange(entry.Date, new DateOnly(startYear, 7, 1), new DateOnly(startYear + 1, 6, 30));
                Assert.True(entry.Through >= entry.Date, $"{entry.Title} ends before it starts");
            }
        }
    }

    private static SchoolCalendar Ascend() =>
        Provider.LoadAll().Single(calendar => calendar.ShortName == "Ascend");

    [Fact]
    public void AscendUsesTheConfirmedYearBoundaries()
    {
        // The school's PDF gave two different answers; the published event list settles it.
        var ascend = Ascend();
        Assert.True(ascend.IsReviewed);
        Assert.Equal(new DateOnly(2026, 8, 5), ascend.Entries.Single(e => e.Kind == SchoolDayKind.FirstDay).Date);
        Assert.Equal(new DateOnly(2027, 5, 21), ascend.Entries.Single(e => e.Kind == SchoolDayKind.LastDay).Date);
    }

    [Fact]
    public void TheSupersededAugust27FirstDayIsGone() =>
        Assert.DoesNotContain(Ascend().On(new DateOnly(2026, 8, 27)), entry => entry.Kind == SchoolDayKind.FirstDay);

    [Fact]
    public void AscendCarriesEveryPublishedEvent() => Assert.Equal(48, Ascend().Entries.Count);

    [Fact]
    public void TimedEventsKeepTheirTimes()
    {
        var openHouse = Ascend().Entries.Single(entry => entry.Title == "ALA Open House");
        Assert.False(openHouse.IsAllDay);
        Assert.Equal(new TimeOnly(15, 0), openHouse.StartTime);
        Assert.Equal("3:00 PM – 6:00 PM", openHouse.TimeText);
        Assert.Equal("3:00 PM – 6:00 PM ALA Open House", openHouse.DisplayTitle);
    }

    [Fact]
    public void AllDayEventsCarryNoTime()
    {
        var firstDay = Ascend().Entries.Single(entry => entry.Kind == SchoolDayKind.FirstDay);
        Assert.True(firstDay.IsAllDay);
        Assert.Equal(string.Empty, firstDay.TimeText);
    }

    [Fact]
    public void MultiDayBreaksSpanTheirFullRange()
    {
        var ascend = Ascend();
        // Winter break crosses the new year.
        Assert.Contains(ascend.On(new DateOnly(2026, 12, 31)), entry => entry.Title == "Winter Break");
        Assert.Contains(ascend.On(new DateOnly(2027, 1, 5)), entry => entry.Title == "Winter Break");
        Assert.DoesNotContain(ascend.On(new DateOnly(2027, 1, 6)), entry => entry.Title == "Winter Break");
        // Spring break crosses a month boundary.
        Assert.Contains(ascend.On(new DateOnly(2027, 4, 2)), entry => entry.Title == "ALA Spring Break");
    }

    [Fact]
    public void ThreeEventsShareMayThirteenth() =>
        Assert.Equal(3, Ascend().On(new DateOnly(2027, 5, 13)).Count());

    [Fact]
    public void SameDayEventsAreOrderedByStartTime()
    {
        // Senior Awards Night (5:30pm) must sort before Senior Sunset (8pm).
        var may13 = Ascend().On(new DateOnly(2027, 5, 13)).Where(entry => !entry.IsAllDay).ToArray();
        Assert.Equal("Senior Awards Night", may13[0].Title);
        Assert.Equal("Senior Sunset", may13[1].Title);
    }

    [Fact]
    public void AscendSpringBreakLinesUpWithGoodFriday()
    {
        // Cross-check against the holiday engine: Good Friday 2027 is 26 March, the day the
        // school's spring break starts.
        var goodFriday = UsHolidays.ForYear(2027).Single(holiday => holiday.Name == "Good Friday");
        Assert.Equal(new DateOnly(2027, 3, 26), goodFriday.Date);
        Assert.Contains(Ascend().On(goodFriday.Date), entry => entry.Title == "ALA Spring Break");
    }
}
