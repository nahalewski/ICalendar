namespace FamilyHub.Core;

public enum HolidayKind
{
    /// <summary>One of the 11 holidays designated by federal law (5 U.S.C. 6103).</summary>
    Federal,
    /// <summary>Widely observed but not a day off — Halloween, Mother's Day, Easter, and the like.</summary>
    Observance
}

/// <param name="Date">The day it is actually marked on the calendar.</param>
/// <param name="ActualDate">The statutory date. Differs from <paramref name="Date"/> only when a fixed
/// federal holiday fell on a weekend and the observed day moved.</param>
public sealed record Holiday(DateOnly Date, string Name, HolidayKind Kind, DateOnly ActualDate)
{
    public bool IsObservedShift => Date != ActualDate;
    public string DisplayName => IsObservedShift ? $"{Name} (observed)" : Name;
}

/// <summary>
/// US federal holidays and common observances, computed rather than fetched so the dashboard is
/// correct offline and in any future year.
/// </summary>
public static class UsHolidays
{
    public static IReadOnlyList<Holiday> ForYear(int year)
    {
        var holidays = new List<Holiday>();

        // Fixed-date federal holidays shift when they land on a weekend; the floating Monday and
        // Thursday ones never do.
        AddFixedFederal(holidays, new DateOnly(year, 1, 1), "New Year's Day");
        AddFederal(holidays, NthWeekday(year, 1, DayOfWeek.Monday, 3), "Martin Luther King Jr. Day");
        AddFederal(holidays, NthWeekday(year, 2, DayOfWeek.Monday, 3), "Presidents' Day");
        AddFederal(holidays, LastWeekday(year, 5, DayOfWeek.Monday), "Memorial Day");
        AddFixedFederal(holidays, new DateOnly(year, 6, 19), "Juneteenth");
        AddFixedFederal(holidays, new DateOnly(year, 7, 4), "Independence Day");
        AddFederal(holidays, NthWeekday(year, 9, DayOfWeek.Monday, 1), "Labor Day");
        AddFederal(holidays, NthWeekday(year, 10, DayOfWeek.Monday, 2), "Columbus Day");
        AddFixedFederal(holidays, new DateOnly(year, 11, 11), "Veterans Day");
        AddFederal(holidays, NthWeekday(year, 11, DayOfWeek.Thursday, 4), "Thanksgiving");
        AddFixedFederal(holidays, new DateOnly(year, 12, 25), "Christmas Day");

        var easter = EasterSunday(year);
        AddObservance(holidays, new DateOnly(year, 2, 14), "Valentine's Day");
        AddObservance(holidays, new DateOnly(year, 3, 17), "St. Patrick's Day");
        AddObservance(holidays, easter.AddDays(-2), "Good Friday");
        AddObservance(holidays, easter, "Easter Sunday");
        AddObservance(holidays, NthWeekday(year, 5, DayOfWeek.Sunday, 2), "Mother's Day");
        AddObservance(holidays, NthWeekday(year, 6, DayOfWeek.Sunday, 3), "Father's Day");
        AddObservance(holidays, new DateOnly(year, 10, 31), "Halloween");
        AddObservance(holidays, new DateOnly(year, 12, 24), "Christmas Eve");
        AddObservance(holidays, new DateOnly(year, 12, 31), "New Year's Eve");

        return holidays.OrderBy(holiday => holiday.Date).ToArray();
    }

    /// <summary>Holidays falling between <paramref name="start"/> and <paramref name="end"/> inclusive.</summary>
    public static IReadOnlyList<Holiday> ForRange(DateOnly start, DateOnly end)
    {
        if (end < start) return [];
        // A New Year's Day that lands on a Saturday is observed on December 31 of the previous year,
        // so the surrounding years are generated too and then filtered.
        return Enumerable.Range(start.Year - 1, end.Year - start.Year + 3)
            .SelectMany(ForYear)
            .Where(holiday => holiday.Date >= start && holiday.Date <= end)
            .OrderBy(holiday => holiday.Date)
            .ToArray();
    }

    /// <summary>Easter Sunday by the anonymous Gregorian computus.</summary>
    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private static void AddFederal(List<Holiday> holidays, DateOnly date, string name) =>
        holidays.Add(new Holiday(date, name, HolidayKind.Federal, date));

    private static void AddObservance(List<Holiday> holidays, DateOnly date, string name) =>
        holidays.Add(new Holiday(date, name, HolidayKind.Observance, date));

    /// <summary>Saturday holidays are observed the Friday before, Sunday holidays the Monday after.</summary>
    private static void AddFixedFederal(List<Holiday> holidays, DateOnly actual, string name)
    {
        var observed = actual.DayOfWeek switch
        {
            DayOfWeek.Saturday => actual.AddDays(-1),
            DayOfWeek.Sunday => actual.AddDays(1),
            _ => actual
        };
        holidays.Add(new Holiday(observed, name, HolidayKind.Federal, actual));
    }

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int occurrence)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (occurrence - 1));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-offset);
    }
}
