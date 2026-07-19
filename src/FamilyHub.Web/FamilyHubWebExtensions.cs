using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FamilyHub.Contracts;
using System.Text.Json;

namespace FamilyHub.Web;

public static class FamilyHubWebExtensions
{
    public static IServiceCollection AddFamilyHubWeb(this IServiceCollection services)
    {
        // TryAdd so a host that already owns the shared state (the desktop app does, to keep the
        // dashboard and the companion API on one instance) wins regardless of registration order.
        services.TryAddSingleton<CompanionControlState>();
        services.AddHealthChecks();
        return services;
    }

    public static WebApplication MapFamilyHub(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapGet("/api/dashboard", (CompanionControlState state) => Results.Ok(state.Snapshot()));
        app.MapPost("/api/control/screen", (ScreenCommandDto command, CompanionControlState state) =>
            state.SetScreen(command.Screen) ? Results.Ok(state.Snapshot()) : Results.BadRequest(new { message = "Screen must be Daily, Weekly, Monthly, Weather, Agenda, Family, ClassBlock, or News." }));
        app.MapPut("/api/settings/rotation", (RotationSettingsDto settings, CompanionControlState state) =>
            state.SetRotation(settings) ? Results.Ok(state.Snapshot()) : Results.BadRequest(new { message = "Rotation must be between 15 and 3600 seconds; inactivity must be between 0 and 3600 seconds." }));
        app.MapGet("/api/events", (CompanionControlState state) => Results.Ok(state.Events));
        app.MapPost("/api/events", (CompanionEventDto item, CompanionControlState state) => Results.Created("/api/events", state.SaveEvent(item)));
        app.MapGet("/api/google/status", () => Results.Ok(new GoogleCalendarStatusDto(false, null, null, "/admin/google/connect", "Google OAuth credentials must be configured on the Pi.")));
        return app;
    }
}

public sealed class CompanionControlState
{
    private readonly object _gate = new();
    private readonly string _storePath;
    private DashboardControlDto _dashboard = new("Daily", 90, true, DateTimeOffset.UtcNow);
    private int _resumeAfterInactivitySeconds = 30;
    private readonly List<CompanionEventDto> _events = [];
    public event EventHandler<string>? ScreenChanged;
    public event EventHandler<RotationSettingsDto>? RotationChanged;
    public event EventHandler<CompanionEventDto>? EventSaved;

    public CompanionControlState() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyHub", "companion-state.json")) { }

    public CompanionControlState(string storePath)
    {
        _storePath = storePath;
        Load();
    }

    public DashboardControlDto Snapshot() { lock (_gate) return _dashboard; }
    public IReadOnlyList<CompanionEventDto> Events { get { lock (_gate) return _events.ToArray(); } }
    public RotationSettingsDto Rotation
    {
        get { lock (_gate) return new RotationSettingsDto(_dashboard.RotationSeconds, _resumeAfterInactivitySeconds, _dashboard.RotationEnabled); }
    }

    public bool SetScreen(string screen)
    {
        if (!new[] { "Daily", "Weekly", "Monthly", "Weather", "Agenda", "Family", "ClassBlock", "News" }.Contains(screen, StringComparer.OrdinalIgnoreCase)) return false;
        lock (_gate) _dashboard = _dashboard with { CurrentScreen = screen, UpdatedAt = DateTimeOffset.UtcNow };
        ScreenChanged?.Invoke(this, screen);
        return true;
    }

    public bool SetRotation(RotationSettingsDto settings)
    {
        if (settings.RotationSeconds is < 15 or > 3600 || settings.ResumeAfterInactivitySeconds is < 0 or > 3600) return false;
        lock (_gate)
        {
            _dashboard = _dashboard with { RotationSeconds = settings.RotationSeconds, RotationEnabled = settings.RotationEnabled, UpdatedAt = DateTimeOffset.UtcNow };
            _resumeAfterInactivitySeconds = settings.ResumeAfterInactivitySeconds;
            Persist();
        }
        RotationChanged?.Invoke(this, settings);
        return true;
    }

    public CompanionEventDto SaveEvent(CompanionEventDto item)
    {
        var saved = item with { Id = item.Id ?? Guid.NewGuid() };
        lock (_gate)
        {
            // Re-saving a known id replaces it, so a retry from the phone cannot duplicate the event.
            var existing = _events.FindIndex(candidate => candidate.Id == saved.Id);
            if (existing >= 0) _events[existing] = saved; else _events.Add(saved);
            Persist();
        }
        EventSaved?.Invoke(this, saved);
        return saved;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var stored = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_storePath));
            if (stored is null) return;
            lock (_gate)
            {
                if (stored.Events is not null) _events.AddRange(stored.Events.Where(item => item is not null));
                if (stored.RotationSeconds is >= 15 and <= 3600)
                    _dashboard = _dashboard with { RotationSeconds = stored.RotationSeconds };
                if (stored.ResumeAfterInactivitySeconds is >= 0 and <= 3600)
                    _resumeAfterInactivitySeconds = stored.ResumeAfterInactivitySeconds;
                _dashboard = _dashboard with { RotationEnabled = stored.RotationEnabled };
            }
        }
        // A corrupt or unreadable store must not stop the dashboard from starting.
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
    }

    /// <summary>Writes the store. Callers already hold <see cref="_gate"/>.</summary>
    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            var payload = new PersistedState(_events.ToArray(), _dashboard.RotationSeconds, _resumeAfterInactivitySeconds, _dashboard.RotationEnabled);
            // Write-then-replace so a crash mid-write cannot leave a truncated store behind.
            var temporary = _storePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload));
            File.Move(temporary, _storePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    private sealed record PersistedState(CompanionEventDto[]? Events, int RotationSeconds, int ResumeAfterInactivitySeconds, bool RotationEnabled);
}
