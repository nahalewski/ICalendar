using FamilyHub.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Infrastructure;

/// <summary>
/// Loads school calendars from a reviewed JSON file rather than scraping district websites.
/// </summary>
/// <remarks>
/// Neither Harnett County Schools nor Ascend Leadership Academy publishes an iCal feed; both post a
/// graphical PDF whose holiday shading carries no extractable text, and the district site rejects
/// automated requests outright. So dates are transcribed once into a file a human can audit, which
/// also means the calendar cannot silently break when a district redesigns its site.
/// </remarks>
public sealed class JsonSchoolCalendarProvider : ISchoolCalendarProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _path;

    public JsonSchoolCalendarProvider(string path) => _path = path;

    /// <summary>Every calendar in the file, including ones still awaiting review.</summary>
    public IReadOnlyList<SchoolCalendar> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return Read(stream);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Only calendars checked against the district's official document.</summary>
    public IReadOnlyList<SchoolCalendar> LoadReviewed() =>
        LoadAll().Where(calendar => calendar.IsReviewed).ToArray();

    public async Task<IReadOnlyList<CalendarEvent>> ImportAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return Read(buffer)
            .SelectMany(calendar => calendar.Entries.Select(entry => ToEvent(calendar, entry)))
            .ToArray();
    }

    private static SchoolCalendar[] Read(Stream stream)
    {
        var file = JsonSerializer.Deserialize<SchoolCalendarFile>(stream, JsonOptions);
        if (file?.Schools is null) return [];
        return file.Schools
            .Where(school => school is not null && !string.IsNullOrWhiteSpace(school.Name))
            .Select(school => new SchoolCalendar(
                school.Name,
                string.IsNullOrWhiteSpace(school.ShortName) ? school.Name : school.ShortName,
                string.IsNullOrWhiteSpace(school.Color) ? "#2563EB" : school.Color,
                school.SchoolYear ?? string.Empty,
                school.Status,
                school.SourceUrl,
                (school.Days ?? [])
                    .Where(day => day is not null && !string.IsNullOrWhiteSpace(day.Title))
                    .Select(day => new SchoolCalendarEntry(day.Date, day.Title, day.Kind, day.EndDate, day.StartTime, day.EndTime))
                    .OrderBy(entry => entry.Date).ThenBy(entry => entry.StartTime ?? TimeOnly.MinValue)
                    .ToArray()))
            .ToArray();
    }

    private static CalendarEvent ToEvent(SchoolCalendar calendar, SchoolCalendarEntry entry) => new(
        ProviderId: "school-json",
        CalendarId: calendar.ShortName,
        Title: $"{calendar.ShortName}: {entry.Title}",
        Start: new DateTimeOffset(entry.Date.ToDateTime(entry.StartTime ?? TimeOnly.MinValue)),
        End: new DateTimeOffset(entry.Through.ToDateTime(entry.EndTime ?? TimeOnly.MaxValue)),
        IsAllDay: entry.IsAllDay,
        Location: null,
        PersonIds: [],
        Category: entry.Kind.ToString(),
        Color: calendar.Color);

    private sealed record SchoolCalendarFile(SchoolEntry[]? Schools);

    private sealed record SchoolEntry(string Name, string? ShortName, string? Color, string? SchoolYear,
        SchoolCalendarStatus Status, string? SourceUrl, DayEntry[]? Days);

    private sealed record DayEntry(DateOnly Date, string Title, SchoolDayKind Kind, DateOnly? EndDate,
        TimeOnly? StartTime, TimeOnly? EndTime);
}
