using FamilyHub.Contracts;
using FamilyHub.Core;
using FamilyHub.Infrastructure;
using System.Globalization;

namespace FamilyHub.App.ViewModels;

/// <summary>
/// Projects the live view model into the flat payload the browser dashboard consumes.
/// </summary>
/// <remarks>
/// Formatting happens here rather than in the browser because the client on the other end may be a
/// Pi Zero W. Block times go out as minutes past midnight so it can animate progress locally
/// instead of polling every second.
/// </remarks>
internal static class DashboardSnapshot
{
    public static ViewStateDto Build(MainWindowViewModel vm, BellScheduleBook schedules,
        Func<DateOnly, string?> closureFor, string currentScreen, int rotationSeconds, bool rotationEnabled)
    {
        var now = vm.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        return new ViewStateDto(
            CurrentScreen: currentScreen,
            ServerTime: now.ToString("h:mm tt", CultureInfo.CurrentCulture),
            ServerMinuteOfDay: now.Hour * 60 + now.Minute,
            TodayLongDate: vm.TodayLongDate,
            MonthTitle: vm.MonthTitle,
            WeekTitle: vm.WeekTitle,
            TodayNote: vm.TodayHolidayText,
            RotationSeconds: rotationSeconds,
            RotationEnabled: rotationEnabled,
            Weather: BuildWeather(vm),
            CalendarDays: vm.CalendarDays.Select(day => new ViewCalendarDayDto(
                day.DayNumber, day.IsCurrentMonth, day.IsToday,
                day.Events.Select(chip => new ViewChipDto(chip.Title, chip.Background)).ToArray())).ToArray(),
            Agenda: vm.AgendaItems.Where(item => item.Date == today)
                .Select(item => new ViewAgendaDto(item.Time, item.Title, item.Person, item.Location, item.Background))
                .ToArray(),
            Students: BuildStudents(vm, schedules, closureFor(today), now),
            ClassBlockHeader: vm.ClassBlockHeader,
            GeneratedAt: DateTimeOffset.Now);
    }

    private static ViewWeatherDto BuildWeather(MainWindowViewModel vm) => new(
        vm.CurrentLocationName, vm.CurrentTemperatureText, vm.CurrentCondition, vm.FeelsLikeText,
        vm.CurrentIcon, vm.WeatherStatus, vm.HumidityText, vm.WindText, vm.GustText,
        vm.CloudCoverText, vm.UvText, vm.SunriseText, vm.SunsetText,
        vm.ForecastDays.Select(day => new ViewForecastDayDto(
            day.Day, day.Date, day.Condition, day.Icon, day.High, day.Low, day.PrecipitationProbability)).ToArray());

    private static ViewStudentDto[] BuildStudents(MainWindowViewModel vm, BellScheduleBook schedules,
        string? closure, DateTimeOffset now)
    {
        var weekday = now.LocalDateTime.DayOfWeek;
        return vm.StudentBlocks.Select(student =>
        {
            var schedule = schedules.For(student.Level);
            var day = schedule?.For(weekday);
            var blocks = day?.Blocks.Select(block => new ViewBlockDto(
                block.Name,
                block.Start.Hour * 60 + block.Start.Minute,
                block.End.Hour * 60 + block.End.Minute,
                block.IsStudentTime)).ToArray() ?? [];

            // An empty block list on a school day means the same thing as a closure to the browser.
            var reason = closure ?? (blocks.Length == 0
                ? (schedule is null ? "No schedule configured" : "No school today")
                : string.Empty);

            return new ViewStudentDto(student.Name, student.Initials, student.LevelText, student.Color,
                student.SchoolName, reason, blocks);
        }).ToArray();
    }

    public static IReadOnlyList<ViewNewsDto> BuildNews(IEnumerable<NewsStory> stories) =>
        stories.Select(story => new ViewNewsDto(story.Title, story.Summary, story.Link,
            story.SourceName, story.SourceLean, story.SourceColor, story.PublishedText, story.Disclaimer)).ToArray();
}
