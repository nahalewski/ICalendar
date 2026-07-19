namespace FamilyHub.Contracts;

// Payload for the browser-based dashboard. Deliberately flat and pre-formatted: the Pi Zero W is a
// single-core ARMv6 running Chromium, so every bit of formatting done here is work its CPU avoids.

public sealed record ViewWeatherDto(
    string Location, string Temperature, string Condition, string FeelsLike, string Icon, string Status,
    string Humidity, string Wind, string Gust, string CloudCover, string Uv, string Sunrise, string Sunset,
    IReadOnlyList<ViewForecastDayDto> Forecast);

public sealed record ViewForecastDayDto(string Day, string Date, string Condition, string Icon, int High, int Low, int RainChance);

public sealed record ViewCalendarDayDto(string Day, bool IsCurrentMonth, bool IsToday, IReadOnlyList<ViewChipDto> Chips);

public sealed record ViewChipDto(string Text, string Color);

public sealed record ViewAgendaDto(string Time, string Title, string Person, string Location, string Color);

/// <param name="Blocks">Today's blocks with start/end as minutes past midnight, so the browser can
/// compute progress locally each second instead of polling the server.</param>
public sealed record ViewStudentDto(string Name, string Initials, string Level, string Color, string SchoolName,
    string ClosureReason, IReadOnlyList<ViewBlockDto> Blocks);

public sealed record ViewBlockDto(string Name, int StartMinute, int EndMinute, bool IsStudentTime);

public sealed record ViewNewsDto(string Title, string Summary, string Link, string Source, string Lean,
    string Color, string Published, string Disclaimer);

public sealed record ViewStateDto(
    string CurrentScreen,
    string ServerTime,
    int ServerMinuteOfDay,
    string TodayLongDate,
    string MonthTitle,
    string WeekTitle,
    string TodayNote,
    int RotationSeconds,
    bool RotationEnabled,
    ViewWeatherDto Weather,
    IReadOnlyList<ViewCalendarDayDto> CalendarDays,
    IReadOnlyList<ViewAgendaDto> Agenda,
    IReadOnlyList<ViewStudentDto> Students,
    string ClassBlockHeader,
    DateTimeOffset GeneratedAt);
