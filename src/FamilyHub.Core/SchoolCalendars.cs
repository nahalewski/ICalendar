namespace FamilyHub.Core;

public enum SchoolDayKind
{
    FirstDay,
    LastDay,
    NoSchool,
    TeacherWorkday,
    EarlyRelease,
    Break,
    Event
}

/// <summary>
/// Whether a transcribed calendar has been checked against the district's official document.
/// Only <see cref="Reviewed"/> sources reach the dashboard — a wall calendar showing a guessed
/// first day of school is worse than showing nothing.
/// </summary>
public enum SchoolCalendarStatus
{
    NeedsReview,
    Reviewed
}

/// <param name="EndDate">Set for multi-day breaks; inclusive. Null for a single day.</param>
/// <param name="StartTime">Null for an all-day entry.</param>
public sealed record SchoolCalendarEntry(DateOnly Date, string Title, SchoolDayKind Kind, DateOnly? EndDate = null,
    TimeOnly? StartTime = null, TimeOnly? EndTime = null)
{
    public DateOnly Through => EndDate ?? Date;
    public bool Covers(DateOnly day) => day >= Date && day <= Through;
    public bool IsAllDay => StartTime is null;

    /// <summary>"5:00 PM – 7:30 PM", or empty for an all-day entry.</summary>
    public string TimeText => StartTime is not { } start
        ? string.Empty
        : EndTime is { } end ? $"{start:h:mm tt} – {end:h:mm tt}" : $"{start:h:mm tt}";

    /// <summary>Title with the time appended, for the single-line chips on the month grid.</summary>
    public string DisplayTitle => IsAllDay ? Title : $"{TimeText} {Title}";
}

public sealed record SchoolCalendar(
    string Name,
    string ShortName,
    string Color,
    string SchoolYear,
    SchoolCalendarStatus Status,
    string? SourceUrl,
    IReadOnlyList<SchoolCalendarEntry> Entries)
{
    public bool IsReviewed => Status == SchoolCalendarStatus.Reviewed;

    public IEnumerable<SchoolCalendarEntry> On(DateOnly day) => Entries.Where(entry => entry.Covers(day));
}
