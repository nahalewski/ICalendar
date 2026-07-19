using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyHub.Contracts;
using FamilyHub.Mobile.Services;
using System.Globalization;
using System.Collections.ObjectModel;

namespace FamilyHub.Mobile.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly FamilyHubClient _client = new();
    private readonly OpenMeteoMobileService _weather = new();
    [ObservableProperty] private string selectedTab = "Remote";
    [ObservableProperty] private string piAddress = "http://192.168.1.177:5643";
    [ObservableProperty] private string currentScreen = "Daily";
    [ObservableProperty] private string rotationSeconds = "90";
    [ObservableProperty] private bool rotationEnabled = true;
    [ObservableProperty] private string eventTitle = string.Empty;
    [ObservableProperty] private string eventLocation = string.Empty;
    [ObservableProperty] private string eventPerson = string.Empty;
    [ObservableProperty] private string eventStartTime = "9:00 AM";
    [ObservableProperty] private string eventEndTime = "10:00 AM";
    [ObservableProperty] private DateOnly selectedEventDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private DateOnly displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string status = "Ready to connect";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string mobileTemperature = "—";
    [ObservableProperty] private string mobileWeatherCondition = "Loading weather";
    [ObservableProperty] private string mobileFeelsLike = "Contacting Open-Meteo";
    [ObservableProperty] private string mobileLocationName = "Sanford, NC";
    [ObservableProperty] private string mobileLocationQuery = string.Empty;
    [ObservableProperty] private string mobileWeatherIcon = "🌤";
    private double _mobileLatitude = 35.4799;
    private double _mobileLongitude = -79.1803;
    public ObservableCollection<MobileForecastDay> Forecast { get; } = [];
    public ObservableCollection<MobileCalendarDay> CalendarDays { get; } = [];
    public ObservableCollection<MobilePerson> People { get; } =
    [
        new("Dad", "D", "#2563EB"), new("Xander", "X", "#F97316"), new("Jada", "J", "#DB2777"),
        new("Allie", "A", "#7C3AED"), new("Gabby", "G", "#0891B2"), new("Elliana", "E", "#16A34A"),
        new("Lilly", "L", "#CA8A04"), new("Sherry", "S", "#DC2626"), new("Benji", "B", "#475569")
    ];
    public string CurrentDateText { get; } = DateTimeOffset.Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    public string MobileMonthTitle => DisplayedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public string SelectedDateText => SelectedEventDate.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    public bool IsRemote => SelectedTab == "Remote";
    public bool IsCalendar => SelectedTab == "Calendar";
    public bool IsWeather => SelectedTab == "Weather";
    public bool IsFamily => SelectedTab == "Family";
    public bool IsSettings => SelectedTab == "Settings";
    partial void OnSelectedTabChanged(string value) { OnPropertyChanged(nameof(IsRemote)); OnPropertyChanged(nameof(IsCalendar)); OnPropertyChanged(nameof(IsWeather)); OnPropertyChanged(nameof(IsFamily)); OnPropertyChanged(nameof(IsSettings)); }

    public MainViewModel() { BuildCalendar(); _ = RefreshWeatherAsync(); }
    [RelayCommand] private void SelectTab(string tab) => SelectedTab = tab;
    private async Task RefreshWeatherAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await _weather.GetAsync(_mobileLatitude, _mobileLongitude, timeout.Token);
            MobileTemperature = $"{snapshot.Temperature}°F"; MobileWeatherCondition = snapshot.Condition; MobileFeelsLike = $"Feels like {snapshot.FeelsLike}°";
            MobileWeatherIcon = snapshot.Icon;
            Forecast.Clear();
            foreach (var day in snapshot.Days) Forecast.Add(new(day.Date == DateOnly.FromDateTime(DateTime.Today) ? "TODAY" : day.Date.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), day.Date.ToString("MMM d", CultureInfo.CurrentCulture), day.Condition, day.High, day.Low, day.RainChance, day.Icon));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            MobileWeatherCondition = "Weather unavailable"; MobileFeelsLike = "Check the network connection";
        }
    }
    [RelayCommand] private async Task ApplyMobileLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(MobileLocationQuery)) { Status = "Enter a city, state, or ZIP code."; return; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var location = await _weather.SearchLocationAsync(MobileLocationQuery, timeout.Token);
            if (location is null) { Status = "Location not found."; return; }
            MobileLocationName = location.Name; _mobileLatitude = location.Latitude; _mobileLongitude = location.Longitude;
            await RefreshWeatherAsync(); Status = $"Weather updated for {location.Name}";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException) { Status = $"Could not update location: {exception.Message}"; }
    }
    [RelayCommand] private async Task ConnectAsync()
    {
        if (!Uri.TryCreate(PiAddress.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) { Status = "Enter a valid Pi address"; return; }
        await RunAsync(async token => { _client.BaseAddress = uri; var state = await _client.GetDashboardAsync(token); if (state is null) return; CurrentScreen = state.CurrentScreen; RotationSeconds = state.RotationSeconds.ToString(CultureInfo.InvariantCulture); RotationEnabled = state.RotationEnabled; Status = "Connected to FamilyHub"; });
    }
    [RelayCommand] private Task ShowScreenAsync(string screen) => RunAsync(async token => { var state = await _client.SetScreenAsync(screen, token); CurrentScreen = state?.CurrentScreen ?? screen; Status = $"Pi showing {CurrentScreen}"; });
    [RelayCommand] private Task SaveSettingsAsync() => RunAsync(async token => { if (!int.TryParse(RotationSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds is < 15 or > 3600) throw new InvalidOperationException("Timer must be 15–3600 seconds."); await _client.SaveRotationAsync(new(seconds, 30, RotationEnabled), token); Status = "Rotation settings saved"; });
    [RelayCommand] private Task AddEventAsync() => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(EventTitle)) throw new InvalidOperationException("Enter an event title.");
        if (!DateTime.TryParse($"{SelectedEventDate:yyyy-MM-dd} {EventStartTime}", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var startLocal) ||
            !DateTime.TryParse($"{SelectedEventDate:yyyy-MM-dd} {EventEndTime}", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var endLocal) || endLocal <= startLocal)
            throw new InvalidOperationException("Enter valid start and end times; the end must be later.");
        var people = string.IsNullOrWhiteSpace(EventPerson) ? Array.Empty<string>() : new[] { EventPerson.Trim() };
        await _client.SaveEventAsync(new(null, EventTitle.Trim(), new DateTimeOffset(startLocal), new DateTimeOffset(endLocal), false, EventLocation.Trim(), people), token);
        EventTitle = string.Empty; EventLocation = string.Empty;
        Status = $"Event added for {SelectedDateText}; it is now visible on FamilyHub.";
    });
    [RelayCommand] private Task CheckGoogleAsync() => RunAsync(async token => { var google = await _client.GetGoogleStatusAsync(token); Status = google?.Message ?? "Google status unavailable"; });
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return; IsBusy = true;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)); await action(timeout.Token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { Status = ex is TaskCanceledException ? "The Pi did not respond" : ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void PreviousMonth() { DisplayedMonth = DisplayedMonth.AddMonths(-1); BuildCalendar(); }
    [RelayCommand] private void NextMonth() { DisplayedMonth = DisplayedMonth.AddMonths(1); BuildCalendar(); }
    [RelayCommand] private void SelectDate(MobileCalendarDay day)
    {
        SelectedEventDate = day.Date;
        OnPropertyChanged(nameof(SelectedDateText));
        BuildCalendar();
    }
    private void BuildCalendar()
    {
        CalendarDays.Clear();
        var first = DisplayedMonth;
        var offset = (int)first.DayOfWeek;
        var gridStart = first.AddDays(-offset);
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            CalendarDays.Add(new(date, date.Day.ToString(CultureInfo.CurrentCulture), date.Month == DisplayedMonth.Month,
                date == SelectedEventDate ? "#6750A4" : "Transparent", date == SelectedEventDate ? "White" : "#201A23"));
        }
        OnPropertyChanged(nameof(MobileMonthTitle));
    }
    public void Dispose() { _client.Dispose(); _weather.Dispose(); }
}

public sealed record MobileForecastDay(string Day, string Date, string Condition, int High, int Low, int RainChance, string Icon = "🌤");
public sealed record MobilePerson(string Name, string Initials, string Color);
public sealed record MobileCalendarDay(DateOnly Date, string DayNumber, bool IsCurrentMonth, string Background, string Foreground);
