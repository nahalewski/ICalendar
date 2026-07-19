namespace FamilyHub.Application;

public static class CalendarDateService
{
    public static DateOnly[] BuildSundayFirstMonthGrid(int year, int month)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        return Enumerable.Range(0, 42).Select(start.AddDays).ToArray();
    }

    public static (DateOnly Start, DateOnly End) GetSundayFirstWeek(DateOnly date)
    {
        var start = date.AddDays(-(int)date.DayOfWeek);
        return (start, start.AddDays(6));
    }
}
