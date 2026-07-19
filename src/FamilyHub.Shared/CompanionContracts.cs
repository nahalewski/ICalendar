namespace FamilyHub.Contracts;

public sealed record DashboardControlDto(string CurrentScreen, int RotationSeconds, bool RotationEnabled, DateTimeOffset UpdatedAt);
public sealed record ScreenCommandDto(string Screen);
public sealed record RotationSettingsDto(int RotationSeconds, int ResumeAfterInactivitySeconds, bool RotationEnabled);
public sealed record CompanionEventDto(Guid? Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, string? Location, IReadOnlyList<string> People);
public sealed record GoogleCalendarStatusDto(bool Connected, string? AccountName, string? CalendarName, string? AuthorizationUrl, string? Message);
