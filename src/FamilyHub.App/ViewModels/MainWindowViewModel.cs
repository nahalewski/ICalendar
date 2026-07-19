using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FamilyHub.Application;
using FamilyHub.Core;
using FamilyHub.Infrastructure;
using System.Collections.ObjectModel;
using System.Globalization;
using FamilyHub.Contracts;
using FamilyHub.Web;
using System.Text.Json;

namespace FamilyHub.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IRotationController _rotation;
    private readonly IWeatherProvider _weatherProvider;
    private readonly CompanionControlState _companion;
    private readonly SchoolCalendar[] _schoolCalendars;
    private readonly BellScheduleBook _scheduleBook;
    private readonly RssNewsProvider _news;
    private readonly Timer _clock;
    private readonly Timer _autoRotate;
    private readonly Timer _weatherRefresh;
    private TimeSpan _rotationInterval = TimeSpan.FromSeconds(90);
    private TimeSpan _resumeDelay = TimeSpan.FromSeconds(30);
    private bool _rotationEnabled = true;
    private readonly string _weatherLocationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyHub", "weather-location.json");
    private double _weatherLatitude = 35.4799;
    private double _weatherLongitude = -79.1803;
    private DateOnly _generatedDate;
    private DateOnly _displayedMonth;
    private IReadOnlyList<Holiday> _holidays = [];

    [ObservableProperty] private DashboardPage currentPage;
    [ObservableProperty] private DateTimeOffset now = DateTimeOffset.Now;
    [ObservableProperty] private string currentTemperatureText = "—";
    [ObservableProperty] private string currentCondition = "Loading weather";
    [ObservableProperty] private string feelsLikeText = "Contacting Open-Meteo";
    [ObservableProperty] private string weatherStatus = "Refreshing";
    [ObservableProperty] private string humidityText = "—";
    [ObservableProperty] private string dewPointText = "—";
    [ObservableProperty] private string windText = "—";
    [ObservableProperty] private string gustText = "—";
    [ObservableProperty] private string visibilityText = "—";
    [ObservableProperty] private string pressureText = "—";
    [ObservableProperty] private string cloudCoverText = "—";
    [ObservableProperty] private string precipitationText = "—";
    [ObservableProperty] private string uvText = "—";
    [ObservableProperty] private string sunriseText = "—";
    [ObservableProperty] private string sunsetText = "—";
    [ObservableProperty] private string selectedPersonName = "Select a family member";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool isFilterPanelOpen;
    [ObservableProperty] private bool isEventEditorOpen;
    [ObservableProperty] private bool searchPeople = true;
    [ObservableProperty] private bool searchEvents = true;
    [ObservableProperty] private bool searchAppointments = true;
    [ObservableProperty] private string newEventTitle = string.Empty;
    [ObservableProperty] private string newEventLocation = string.Empty;
    [ObservableProperty] private string newEventPerson = string.Empty;
    [ObservableProperty] private string newEventCategory = string.Empty;
    [ObservableProperty] private string newEventStart = DateTime.Now.AddHours(1).ToString("g", CultureInfo.CurrentCulture);
    [ObservableProperty] private string newEventEnd = DateTime.Now.AddHours(2).ToString("g", CultureInfo.CurrentCulture);
    [ObservableProperty] private string eventEditorError = string.Empty;
    [ObservableProperty] private bool isDarkMode;
    [ObservableProperty] private string weatherLocationQuery = string.Empty;
    [ObservableProperty] private string currentLocationName = "Sanford, NC";
    [ObservableProperty] private string currentIcon = "🌤";

    public MainWindowViewModel(IRotationController rotation, IWeatherProvider weatherProvider, CompanionControlState companion)
    {
        _rotation = rotation;
        _weatherProvider = weatherProvider;
        _companion = companion;
        var schoolProvider = new JsonSchoolCalendarProvider(DataFile.Resolve("school-calendars.json"));
        var allSchools = schoolProvider.LoadAll();
        _schoolCalendars = allSchools.Where(calendar => calendar.IsReviewed).ToArray();
        PendingSchoolCalendars = allSchools.Count - _schoolCalendars.Length;
        _scheduleBook = new JsonBellScheduleProvider(DataFile.Resolve("bell-schedules.json")).Load();
        _news = new RssNewsProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        _rotation.PageChanged += OnPageChanged;
        CalendarDays = new ObservableCollection<CalendarDayViewModel>();
        AgendaItems = new ObservableCollection<AgendaItemViewModel>();
        ForecastDays = new ObservableCollection<ForecastDayViewModel>();
        People = new ObservableCollection<PersonCardViewModel>();
        SelectedPersonEvents = new ObservableCollection<AgendaItemViewModel>();
        SearchResults = new ObservableCollection<SearchResultViewModel>();
        LoadWeatherLocation();
        GenerateDateDrivenContent(DateOnly.FromDateTime(Now.LocalDateTime));
        SeedPeople();
        ApplyRotationSettings(_companion.Rotation);
        BuildClassBlocks();
        // Replay events already on disk (saved earlier from this app or the companion) so they
        // survive a restart instead of vanishing with the in-memory list.
        foreach (var stored in _companion.Events) AddCompanionEvent(stored);
        _ = RefreshWeatherAsync();
        _clock = new Timer(_ => Dispatcher.UIThread.Post(UpdateClock), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _autoRotate = new Timer(_ => Dispatcher.UIThread.Post(() =>
            { if (_rotationEnabled) _rotation.TryRotate(DateTimeOffset.UtcNow, _rotationInterval, _resumeDelay); }),
            null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        _weatherRefresh = new Timer(_ => _ = RefreshWeatherAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task RefreshWeatherAsync()
    {
        try
        {
            var (current, forecast) = await _weatherProvider.GetAsync(_weatherLatitude, _weatherLongitude, CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentTemperatureText = $"{Math.Round(current.Temperature):0}°F";
                CurrentCondition = current.Condition;
                CurrentIcon = WeatherGlyph.For(current.WeatherCode);
                FeelsLikeText = $"Feels like {Math.Round(current.FeelsLike):0}°";
                WeatherStatus = current.IsCached ? $"Cached • updated {current.UpdatedAt:g}" : $"Live • updated {current.UpdatedAt:t}";
                HumidityText = $"{current.Humidity}%";
                DewPointText = $"{Math.Round(current.DewPoint):0}°F";
                WindText = $"{Math.Round(current.WindSpeed):0} mph";
                GustText = $"Gust {Math.Round(current.WindGust):0} mph";
                VisibilityText = $"{current.VisibilityMiles:0.0} mi";
                PressureText = $"{current.Pressure / 33.8639:0.00} inHg";
                CloudCoverText = $"{current.CloudCover}%";
                PrecipitationText = $"{current.Precipitation:0.00} in";
                ForecastDays.Clear();
                foreach (var day in forecast)
                    ForecastDays.Add(new(day.Date.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), day.Date.ToString("MMM d", CultureInfo.CurrentCulture), day.Condition,
                        (int)Math.Round(day.High), (int)Math.Round(day.Low), day.PrecipitationProbability, day.PrecipitationAmount,
                        (int)Math.Round(day.MaxWindSpeed), (int)Math.Round(day.WindGust), day.Humidity, (int)Math.Round(day.UvIndex), day.Sunrise, day.Sunset,
                        WeatherGlyph.For(day.WeatherCode)));
                if (forecast.Count > 0)
                {
                    UvText = forecast[0].UvIndex.ToString("0.0", CultureInfo.CurrentCulture);
                    SunriseText = forecast[0].Sunrise;
                    SunsetText = forecast[0].Sunset;
                }
            });
        }
        // A partial or reshaped Open-Meteo payload surfaces here as an indexing/format failure. The
        // refresh is fire-and-forget, so anything uncaught would silently freeze weather for good.
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException
            or JsonException or IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException or NullReferenceException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => WeatherStatus = $"Weather unavailable • {exception.Message}");
        }
    }

    [RelayCommand] private async Task ApplyWeatherLocationAsync()
    {
        WeatherStatus = "Finding location…";
        try
        {
            var query = WeatherLocationQuery.Trim();
            var location = await _weatherProvider.SearchLocationAsync(query, CancellationToken.None);
            if (location is null) { WeatherStatus = "Location not found. Try a ZIP code, \"City, State\", or latitude,longitude."; return; }
            _weatherLatitude = location.Latitude; _weatherLongitude = location.Longitude; CurrentLocationName = location.Name;
            Directory.CreateDirectory(Path.GetDirectoryName(_weatherLocationPath)!);
            await File.WriteAllTextAsync(_weatherLocationPath, JsonSerializer.Serialize(new WeatherLocationSettings(location.Name, location.Latitude, location.Longitude, query)));
            await RefreshWeatherAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
        { WeatherStatus = $"Could not update location: {exception.Message}"; }
    }

    [RelayCommand] private Task RefreshWeatherNowAsync() => RefreshWeatherAsync();

    // ---- Class Block -------------------------------------------------------------------------

    public ObservableCollection<StudentBlockViewModel> StudentBlocks { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> ScheduleRows { get; } = [];
    [ObservableProperty] private string classBlockHeader = "Class blocks";
    [ObservableProperty] private string classBlockSourceNote = string.Empty;

    /// <summary>Recomputed every second from the clock so each child's block advances live.</summary>
    private void UpdateClassBlocks()
    {
        if (_scheduleBook.Students.Count == 0) return;
        var now = Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var closure = SchoolClosureReason(today);

        foreach (var student in StudentBlocks)
            student.Update(BellScheduleService.Evaluate(_scheduleBook.For(student.Level), now, closure));

        var reference = _scheduleBook.For(SchoolLevel.High) ?? (_scheduleBook.Schedules.Count > 0 ? _scheduleBook.Schedules[0] : null);
        var day = reference?.For(now.DayOfWeek);
        ClassBlockHeader = closure is not null ? closure
            : day is null ? "No school today"
            : $"{day.Name} • {now:dddd, MMMM d}";

        var time = TimeOnly.FromDateTime(now.LocalDateTime);
        foreach (var row in ScheduleRows) row.Update(time, closure is null);
    }

    /// <summary>A break or holiday on a reviewed school calendar overrides the bell schedule.</summary>
    private string? SchoolClosureReason(DateOnly day)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return "Weekend — no school";
        foreach (var calendar in _schoolCalendars)
            foreach (var entry in calendar.On(day))
                if (entry.Kind is SchoolDayKind.NoSchool or SchoolDayKind.Break)
                    return entry.Title;
        return null;
    }

    private void BuildClassBlocks()
    {
        foreach (var student in _scheduleBook.Students)
            StudentBlocks.Add(new StudentBlockViewModel(student.Name, student.Level, student.Color,
                _scheduleBook.For(student.Level)?.SchoolName ?? "No schedule configured"));

        var reference = _scheduleBook.For(SchoolLevel.High) ?? (_scheduleBook.Schedules.Count > 0 ? _scheduleBook.Schedules[0] : null);
        ClassBlockSourceNote = reference is null
            ? "No bell schedule file loaded."
            : $"Bell schedule as published for {reference.SourceYear}.";
        RebuildScheduleRows();
    }

    private void RebuildScheduleRows()
    {
        ScheduleRows.Clear();
        var day = Now.DayOfWeek;
        foreach (var schedule in _scheduleBook.Schedules)
        {
            var today = schedule.For(day);
            if (today is null) continue;
            foreach (var block in today.Blocks)
                ScheduleRows.Add(new ScheduleRowViewModel(schedule.Level.ToString(), block));
        }
    }

    // ---- News --------------------------------------------------------------------------------

    public ObservableCollection<NewsStory> NewsStories { get; } = [];
    [ObservableProperty] private string newsStatus = "Loading headlines…";
    [ObservableProperty] private bool isNewsLoading;

    [RelayCommand] private async Task RefreshNewsAsync()
    {
        if (IsNewsLoading) return;
        IsNewsLoading = true;
        NewsStatus = "Fetching headlines…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var stories = await _news.GetAsync(RssNewsProvider.DefaultSources, 4, timeout.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewsStories.Clear();
                foreach (var story in stories) NewsStories.Add(story);
                var outlets = stories.Select(story => story.SourceName).Distinct().Count();
                NewsStatus = stories.Count == 0
                    ? "No headlines available — check the network connection."
                    : $"{stories.Count} headlines from {outlets} outlets • updated {DateTimeOffset.Now:t}";
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => NewsStatus = $"Headlines unavailable • {exception.Message}");
        }
        finally { IsNewsLoading = false; }
    }

    /// <summary>Applies rotation settings saved from the Android companion app.</summary>
    public void ApplyRotationSettings(RotationSettingsDto settings)
    {
        _rotationInterval = TimeSpan.FromSeconds(settings.RotationSeconds);
        _resumeDelay = TimeSpan.FromSeconds(settings.ResumeAfterInactivitySeconds);
        _rotationEnabled = settings.RotationEnabled;
    }

    private void LoadWeatherLocation()
    {
        try
        {
            if (!File.Exists(_weatherLocationPath)) return;
            var saved = JsonSerializer.Deserialize<WeatherLocationSettings>(File.ReadAllText(_weatherLocationPath));
            if (saved is null || saved.Latitude is < -90 or > 90 || saved.Longitude is < -180 or > 180) return;
            CurrentLocationName = saved.Name; _weatherLatitude = saved.Latitude; _weatherLongitude = saved.Longitude;
            // Show the saved ZIP back in the Settings box so it is visible and editable, not blank.
            WeatherLocationQuery = string.IsNullOrWhiteSpace(saved.Query) ? saved.Name : saved.Query;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { WeatherStatus = $"Using default location: {exception.Message}"; }
    }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; }
    public ObservableCollection<AgendaItemViewModel> AgendaItems { get; }
    public ObservableCollection<ForecastDayViewModel> ForecastDays { get; }
    public ObservableCollection<PersonCardViewModel> People { get; }
    public ObservableCollection<AgendaItemViewModel> SelectedPersonEvents { get; }
    public ObservableCollection<SearchResultViewModel> SearchResults { get; }
    public bool HasSearchResults => SearchResults.Count > 0 && !string.IsNullOrWhiteSpace(SearchText);
    public string TimeText => Now.ToString("h:mm tt", CultureInfo.CurrentCulture);
    public string DateText => Now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
    public string MonthTitle => _displayedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public string TodayLongDate => Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    public string WeekTitle
    {
        get
        {
            var date = DateOnly.FromDateTime(Now.LocalDateTime);
            var (start, end) = CalendarDateService.GetSundayFirstWeek(date);
            return $"{start:MMM d} – {end:MMM d, yyyy}";
        }
    }

    public bool IsDaily => CurrentPage == DashboardPage.Daily;
    public bool IsWeekly => CurrentPage == DashboardPage.Weekly;
    public bool IsMonthly => CurrentPage == DashboardPage.Monthly;
    public bool IsWeather => CurrentPage == DashboardPage.Weather;
    public bool IsAgenda => CurrentPage == DashboardPage.Agenda;
    public bool IsFamily => CurrentPage == DashboardPage.Family;
    public bool IsSettings => CurrentPage == DashboardPage.Settings;
    public bool IsClassBlock => CurrentPage == DashboardPage.ClassBlock;
    public bool IsNews => CurrentPage == DashboardPage.News;
    /// <summary>School calendars transcribed but not yet checked against the district's document.</summary>
    public int PendingSchoolCalendars { get; }
    public bool HasPendingSchoolCalendars => PendingSchoolCalendars > 0;
    public string SchoolCalendarStatusText => _schoolCalendars.Length == 0 && PendingSchoolCalendars == 0
        ? "No school calendars configured."
        : $"{_schoolCalendars.Length} school calendar(s) live" +
          (PendingSchoolCalendars > 0 ? $" • {PendingSchoolCalendars} awaiting review in school-calendars.json" : string.Empty);
    /// <summary>Pages that own editable fields, where gestures and shortcuts must not steal input.</summary>
    public bool IsTextEntryActive => IsSettings || IsEventEditorOpen;

    partial void OnNowChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TimeText)); OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(MonthTitle)); OnPropertyChanged(nameof(TodayLongDate)); OnPropertyChanged(nameof(WeekTitle));
        OnPropertyChanged(nameof(TodayHolidayText)); OnPropertyChanged(nameof(HasTodayHoliday));
    }

    partial void OnCurrentPageChanged(DashboardPage value)
    {
        OnPropertyChanged(nameof(IsDaily)); OnPropertyChanged(nameof(IsWeekly)); OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsWeather)); OnPropertyChanged(nameof(IsAgenda)); OnPropertyChanged(nameof(IsFamily));
        OnPropertyChanged(nameof(IsSettings)); OnPropertyChanged(nameof(IsTextEntryActive));
        OnPropertyChanged(nameof(IsClassBlock)); OnPropertyChanged(nameof(IsNews));
        if (IsNews && NewsStories.Count == 0) _ = RefreshNewsAsync();
    }

    partial void OnIsEventEditorOpenChanged(bool value) => OnPropertyChanged(nameof(IsTextEntryActive));

    partial void OnSearchTextChanged(string value) => RebuildSearchResults();
    partial void OnSearchPeopleChanged(bool value) => RebuildSearchResults();
    partial void OnSearchEventsChanged(bool value) => RebuildSearchResults();
    partial void OnSearchAppointmentsChanged(bool value) => RebuildSearchResults();

    private void UpdateClock()
    {
        Now = DateTimeOffset.Now;
        IsDarkMode = Now.Hour is >= 18 or < 6;
        if (Avalonia.Application.Current is App app) app.ApplyAutomaticTheme(Now);
        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        if (today != _generatedDate) { GenerateDateDrivenContent(today); RebuildScheduleRows(); }
        UpdateClassBlocks();
    }

    private void GenerateDateDrivenContent(DateOnly today)
    {
        _generatedDate = today;
        _displayedMonth = new DateOnly(today.Year, today.Month, 1);
        // ForecastDays is owned by the weather refresh, not the calendar date; clearing it here left
        // the forecast blank from midnight until the next poll.
        AgendaItems.Clear(); SelectedPersonEvents.Clear();
        GenerateMonth();
    }

    private void GenerateMonth()
    {
        CalendarDays.Clear();
        var monthGrid = CalendarDateService.BuildSundayFirstMonthGrid(_displayedMonth.Year, _displayedMonth.Month);
        _holidays = UsHolidays.ForRange(monthGrid[0], monthGrid[^1]);
        for (var index = 0; index < monthGrid.Length; index++)
        {
            var date = monthGrid[index];
            var isToday = date == _generatedDate;
            var isCurrentMonth = date.Month == _displayedMonth.Month;
            CalendarDays.Add(new CalendarDayViewModel(date.Day.ToString(CultureInfo.CurrentCulture),
                isCurrentMonth, isToday, isToday ? "#FF5C60" : "Transparent", isToday ? "White" : "#092354",
                isCurrentMonth ? 1d : 0.35d, ChipsFor(date)) { Date = date });
        }
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(TodayHolidayText));
    }

    /// <summary>Holidays and school days sit behind family events so the day's own plans read first.</summary>
    private IEnumerable<EventChipViewModel> ChipsFor(DateOnly date)
    {
        foreach (var holiday in _holidays.Where(holiday => holiday.Date == date))
            yield return new EventChipViewModel(holiday.DisplayName,
                holiday.Kind == HolidayKind.Federal ? "#FFE1E4" : "#EDE7FB", "#092354");
        foreach (var calendar in _schoolCalendars)
            foreach (var entry in calendar.On(date))
                yield return new EventChipViewModel($"{calendar.ShortName}: {entry.DisplayTitle}", calendar.Color, "White");
        foreach (var item in AgendaItems.Where(item => item.Date == date))
            yield return new EventChipViewModel(item.Title, item.Background, "#092354");
    }

    /// <summary>What today is, if it is anything — shown on the Today and Agenda pages.</summary>
    public string TodayHolidayText
    {
        get
        {
            var names = UsHolidays.ForRange(_generatedDate, _generatedDate).Select(holiday => holiday.DisplayName)
                .Concat(_schoolCalendars.SelectMany(calendar => calendar.On(_generatedDate)
                    .Select(entry => $"{calendar.ShortName}: {entry.DisplayTitle}")))
                .ToArray();
            return names.Length == 0 ? string.Empty : string.Join(" • ", names);
        }
    }
    public bool HasTodayHoliday => TodayHolidayText.Length > 0;

    private void SeedPeople()
    {
        foreach (var person in FamilyHub.Core.SeedPeople.Create())
            People.Add(new(person.DisplayName, person.Initials, person.Color, string.Join(" • ", person.Aliases), 0));
    }

    private void OnPageChanged(object? sender, DashboardPage page) => CurrentPage = page;
    [RelayCommand] private void Next() { Interact(); _rotation.MoveNext(); }
    [RelayCommand] private void Previous() { Interact(); _rotation.MovePrevious(); }
    [RelayCommand] private void Home() { Interact(); _rotation.Navigate(DashboardPage.Daily); }
    [RelayCommand] private void Navigate(string page) { Interact(); if (Enum.TryParse<DashboardPage>(page, true, out var value)) _rotation.Navigate(value); }
    [RelayCommand] private void SelectPerson(PersonCardViewModel person)
    {
        SelectedPersonName = person.Name;
        SelectedPersonEvents.Clear();
        var names = person.Aliases.Split('•', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(person.Name);
        foreach (var item in AgendaItems.Where(item => names.Any(name => item.Person.Equals(name, StringComparison.OrdinalIgnoreCase) || item.Title.Contains(name, StringComparison.OrdinalIgnoreCase))))
            SelectedPersonEvents.Add(item);
    }
    [RelayCommand] private void ToggleFilters() => IsFilterPanelOpen = !IsFilterPanelOpen;
    [RelayCommand] private void OpenEventEditor() { IsEventEditorOpen = true; EventEditorError = string.Empty; }
    [RelayCommand] private void CloseEventEditor() => IsEventEditorOpen = false;
    [RelayCommand] private void OpenSearchResult(SearchResultViewModel result)
    {
        SearchText = string.Empty;
        if (result.Kind == "Person")
        {
            var person = People.FirstOrDefault(p => p.Name == result.Title);
            if (person is not null) { _rotation.Navigate(DashboardPage.Family); SelectPerson(person); }
        }
        else _rotation.Navigate(DashboardPage.Agenda);
    }
    [RelayCommand] private void SaveNewEvent()
    {
        EventEditorError = string.Empty;
        if (string.IsNullOrWhiteSpace(NewEventTitle)) { EventEditorError = "Enter an event title."; return; }
        if (!DateTime.TryParse(NewEventStart, CultureInfo.CurrentCulture, out var start) || !DateTime.TryParse(NewEventEnd, CultureInfo.CurrentCulture, out var end) || end <= start)
        { EventEditorError = "Enter valid start and end times; the end must be after the start."; return; }
        // Saved through the companion store so the Android app can see events created here, and so
        // they survive a restart. AddCompanionEvent renders it and ignores the echoed EventSaved.
        var people = string.IsNullOrWhiteSpace(NewEventPerson) ? Array.Empty<string>() : new[] { NewEventPerson.Trim() };
        var saved = _companion.SaveEvent(new CompanionEventDto(null, NewEventTitle.Trim(), new DateTimeOffset(start),
            new DateTimeOffset(end), false, NewEventLocation.Trim(), people));
        AddCompanionEvent(saved, NewEventCategory.Trim());
        IsEventEditorOpen = false;
        NewEventTitle = NewEventLocation = NewEventPerson = NewEventCategory = string.Empty;
    }
    [RelayCommand] private void PreviousMonth() { _displayedMonth = _displayedMonth.AddMonths(-1); GenerateMonth(); }
    [RelayCommand] private void NextMonth() { _displayedMonth = _displayedMonth.AddMonths(1); GenerateMonth(); }
    [RelayCommand] private void CurrentMonth() { _displayedMonth = new DateOnly(_generatedDate.Year, _generatedDate.Month, 1); GenerateMonth(); }
    public void AddCompanionEvent(CompanionEventDto saved, string category = "")
    {
        // The same event arrives twice when it originates here: once directly, once via EventSaved.
        // Matching on id also absorbs a retry from the phone.
        if (saved.Id is { } id && AgendaItems.Any(existing => existing.Id == id)) return;
        var personName = saved.People.Count > 0 ? saved.People[0] : string.Empty;
        var person = People.FirstOrDefault(p => p.Name.Equals(personName, StringComparison.OrdinalIgnoreCase));
        var item = new AgendaItemViewModel(saved.IsAllDay ? "All day" : $"{saved.Start.LocalDateTime:h:mm tt} – {saved.End.LocalDateTime:h:mm tt}", saved.Title,
            saved.Location ?? string.Empty, person?.Color ?? "#DDEEFF", person?.Initials ?? "EV", personName, category,
            DateOnly.FromDateTime(saved.Start.LocalDateTime), saved.Id ?? Guid.NewGuid());
        AgendaItems.Add(item);
        var day = CalendarDays.FirstOrDefault(d => d.Date == item.Date);
        day?.Events.Add(new EventChipViewModel(item.Title, item.Background, "#092354"));
        if (!string.IsNullOrWhiteSpace(SelectedPersonName) && item.Person.Equals(SelectedPersonName, StringComparison.OrdinalIgnoreCase)) SelectedPersonEvents.Add(item);
        RebuildSearchResults();
    }
    private void RebuildSearchResults()
    {
        SearchResults.Clear();
        var query = SearchText.Trim();
        if (query.Length == 0) { OnPropertyChanged(nameof(HasSearchResults)); return; }
        if (SearchPeople)
            foreach (var p in People.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Aliases.Contains(query, StringComparison.OrdinalIgnoreCase)))
                SearchResults.Add(new("Person", p.Name, $"Aliases: {p.Aliases}"));
        foreach (var item in AgendaItems.Where(e => (SearchEvents && (e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Location.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Person.Contains(query, StringComparison.OrdinalIgnoreCase))) || (SearchAppointments && e.Category.Contains(query, StringComparison.OrdinalIgnoreCase))))
            SearchResults.Add(new(string.IsNullOrWhiteSpace(item.Category) ? "Event" : "Appointment", item.Title, $"{item.Time} • {item.Person} • {item.Category}"));
        OnPropertyChanged(nameof(HasSearchResults));
    }
    public void Interact() => _rotation.RecordInteraction(DateTimeOffset.UtcNow);
    public void Dispose() { _rotation.PageChanged -= OnPageChanged; _clock.Dispose(); _autoRotate.Dispose(); _weatherRefresh.Dispose(); }
}

/// <summary>Saved home location. <paramref name="Query"/> is what the person actually typed (usually a
/// ZIP code) so the Settings box can be restored exactly as they left it.</summary>
public sealed record WeatherLocationSettings(string Name, double Latitude, double Longitude, string Query = "");

/// <summary>One child's live position in the school day.</summary>
public sealed partial class StudentBlockViewModel(string name, SchoolLevel level, string color, string schoolName)
    : ObservableObject
{
    public string Name { get; } = name;
    public SchoolLevel Level { get; } = level;
    public string Color { get; } = color;
    public string SchoolName { get; } = schoolName;
    public string Initials { get; } = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
    public string LevelText { get; } = level switch
    {
        SchoolLevel.Elementary => "Elementary",
        SchoolLevel.Middle => "Middle school",
        _ => "High school"
    };

    [ObservableProperty] private string headline = string.Empty;
    [ObservableProperty] private string detail = string.Empty;
    [ObservableProperty] private string nextUp = string.Empty;
    [ObservableProperty] private double blockFraction;
    [ObservableProperty] private double dayFraction;
    [ObservableProperty] private bool isInSchool;
    [ObservableProperty] private double blockBarWidth;
    [ObservableProperty] private double dayBarWidth;

    /// <summary>Bars are drawn as a fixed-width track with a proportional fill.</summary>
    private const double TrackWidth = 300;

    public void Update(BlockProgress progress)
    {
        Headline = progress.Headline;
        Detail = progress.Detail;
        IsInSchool = progress.IsInSchool;
        BlockFraction = progress.FractionThroughBlock;
        DayFraction = progress.FractionThroughDay;
        BlockBarWidth = TrackWidth * progress.FractionThroughBlock;
        DayBarWidth = TrackWidth * progress.FractionThroughDay;
        NextUp = progress.Next is { } next && progress.State != BlockDayState.AfterSchool
            ? $"Next: {next.Name} at {next.Start:h:mm tt}"
            : string.Empty;
    }
}

/// <summary>A row of today's schedule, highlighted as the day reaches it.</summary>
public sealed partial class ScheduleRowViewModel(string level, ScheduleBlock block) : ObservableObject
{
    public string Level { get; } = level;
    public string Name { get; } = block.Name;
    public string TimeText { get; } = block.TimeText;
    public bool IsStaffOnly { get; } = !block.IsStudentTime;
    private readonly ScheduleBlock _block = block;

    [ObservableProperty] private bool isCurrent;
    [ObservableProperty] private bool isPast;
    /// <summary>Exposed as plain values so the view needs no converters.</summary>
    [ObservableProperty] private string rowBackground = "Transparent";
    [ObservableProperty] private double rowOpacity = 1d;

    public void Update(TimeOnly now, bool schoolInSession)
    {
        IsCurrent = schoolInSession && _block.Contains(now);
        IsPast = schoolInSession && now >= _block.End;
        RowBackground = IsCurrent ? "#FFF1D6" : "Transparent";
        RowOpacity = IsPast ? 0.4 : 1d;
    }
}

public sealed record EventChipViewModel(string Title, string Background, string Foreground);
public sealed class CalendarDayViewModel(string dayNumber, bool isCurrentMonth, bool isToday, string dayBackground,
    string dayForeground, double opacity, IEnumerable<EventChipViewModel> events)
{
    public string DayNumber { get; } = dayNumber; public bool IsCurrentMonth { get; } = isCurrentMonth; public bool IsToday { get; } = isToday;
    public string DayBackground { get; } = dayBackground; public string DayForeground { get; } = dayForeground; public double Opacity { get; } = opacity;
    public DateOnly Date { get; set; }
    public ObservableCollection<EventChipViewModel> Events { get; } = new(events);
}
public sealed record AgendaItemViewModel(string Time, string Title, string Location, string Background, string Initials, string Person = "", string Category = "", DateOnly Date = default, Guid Id = default);
public sealed record ForecastDayViewModel(string Day, string Date, string Condition, int High, int Low, int PrecipitationProbability,
    double PrecipitationInches, int MaxWindMph, int GustMph, int Humidity, int UvIndex, string Sunrise, string Sunset, string Icon = "🌤");
public sealed record PersonCardViewModel(string Name, string Initials, string Color, string Aliases, int EventCount);
public sealed record SearchResultViewModel(string Kind, string Title, string Subtitle);
