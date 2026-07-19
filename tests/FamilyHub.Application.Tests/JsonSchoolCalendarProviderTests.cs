using FamilyHub.Core;
using FamilyHub.Infrastructure;
using Xunit;

namespace FamilyHub.Application.Tests;

public sealed class JsonSchoolCalendarProviderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"familyhub-schools-{Guid.NewGuid():N}.json");

    private JsonSchoolCalendarProvider Write(string json)
    {
        File.WriteAllText(_path, json);
        return new JsonSchoolCalendarProvider(_path);
    }

    private const string TwoSchools = """
    {
      "schools": [
        {
          "name": "Harnett County Schools", "shortName": "HCS", "color": "#2563EB",
          "schoolYear": "2026-2027", "status": "Reviewed",
          "days": [
            { "date": "2026-08-12", "title": "First day of school", "kind": "FirstDay" },
            { "date": "2026-12-21", "endDate": "2027-01-05", "title": "Winter break", "kind": "Break" }
          ]
        },
        {
          "name": "Ascend Leadership Academy", "shortName": "Ascend", "color": "#7C3AED",
          "schoolYear": "2026-2027", "status": "NeedsReview",
          "days": [ { "date": "2026-08-27", "title": "First day of school", "kind": "FirstDay" } ]
        }
      ]
    }
    """;

    [Fact]
    public void UnreviewedCalendarsAreHeldBack()
    {
        var provider = Write(TwoSchools);
        Assert.Equal(2, provider.LoadAll().Count);

        // The guard that matters: an unverified calendar must never reach the wall display.
        var live = Assert.Single(provider.LoadReviewed());
        Assert.Equal("Harnett County Schools", live.Name);
    }

    [Fact]
    public void MultiDayBreakCoversEveryDayInclusive()
    {
        var winter = Write(TwoSchools).LoadReviewed()[0].Entries.Single(entry => entry.Kind == SchoolDayKind.Break);
        Assert.True(winter.Covers(new DateOnly(2026, 12, 21)));   // first day
        Assert.True(winter.Covers(new DateOnly(2026, 12, 30)));   // middle
        Assert.True(winter.Covers(new DateOnly(2027, 1, 5)));     // last day
        Assert.False(winter.Covers(new DateOnly(2027, 1, 6)));    // back to school
    }

    [Fact]
    public void SingleDayEntryCoversOnlyItself()
    {
        var firstDay = Write(TwoSchools).LoadReviewed()[0].Entries.Single(entry => entry.Kind == SchoolDayKind.FirstDay);
        Assert.True(firstDay.Covers(new DateOnly(2026, 8, 12)));
        Assert.False(firstDay.Covers(new DateOnly(2026, 8, 13)));
    }

    [Fact]
    public void OnReturnsEntriesForADay()
    {
        var calendar = Write(TwoSchools).LoadReviewed()[0];
        Assert.Single(calendar.On(new DateOnly(2026, 12, 25)));
        Assert.Empty(calendar.On(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        // The shipped file is annotated with provenance notes, so it must survive comments.
        var provider = Write("""
        // where these dates came from
        { "schools": [ { "name": "Test", "status": "Reviewed", "days": [
            { "date": "2026-08-12", "title": "First day", "kind": "FirstDay" },
        ] } ] }
        """);
        Assert.Single(provider.LoadReviewed()[0].Entries);
    }

    [Fact]
    public void EntriesAreSortedByDate()
    {
        var provider = Write("""
        { "schools": [ { "name": "Test", "status": "Reviewed", "days": [
            { "date": "2027-05-21", "title": "Last day", "kind": "LastDay" },
            { "date": "2026-08-12", "title": "First day", "kind": "FirstDay" }
        ] } ] }
        """);
        var entries = provider.LoadReviewed()[0].Entries;
        Assert.Equal(new DateOnly(2026, 8, 12), entries[0].Date);
    }

    [Fact]
    public void MissingFileYieldsNothingRatherThanThrowing() =>
        Assert.Empty(new JsonSchoolCalendarProvider(Path.Combine(Path.GetTempPath(), "does-not-exist.json")).LoadAll());

    [Fact]
    public void MalformedFileYieldsNothingRatherThanThrowing() =>
        Assert.Empty(Write("{ not json at all").LoadAll());

    [Fact]
    public async Task ImportProducesAllDayCalendarEvents()
    {
        var provider = Write(TwoSchools);
        await using var stream = File.OpenRead(_path);
        var events = await provider.ImportAsync(stream, CancellationToken.None);

        var firstDay = events.Single(item => item.Title == "HCS: First day of school");
        Assert.True(firstDay.IsAllDay);
        Assert.Equal("FirstDay", firstDay.Category);
        Assert.Equal("#2563EB", firstDay.Color);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
