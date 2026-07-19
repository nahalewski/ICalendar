namespace FamilyHub.Core;

public enum SchoolLevel { Elementary, Middle, High }

public enum BlockKind
{
    Class,
    Lunch,
    Advisory,
    /// <summary>Staff time such as teacher PD — students are not in the building yet.</summary>
    StaffOnly
}

public sealed record ScheduleBlock(string Name, TimeOnly Start, TimeOnly End, BlockKind Kind = BlockKind.Class)
{
    public TimeSpan Length => End - Start;
    public string TimeText => $"{Start:h:mm} – {End:h:mm tt}";
    public bool Contains(TimeOnly time) => time >= Start && time < End;
    /// <summary>Staff blocks are shown greyed out rather than counted as part of a child's day.</summary>
    public bool IsStudentTime => Kind != BlockKind.StaffOnly;
}

/// <summary>One day's block layout — schools commonly run a different one on Wednesdays.</summary>
public sealed record DailyBellSchedule(string Name, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<ScheduleBlock> Blocks)
{
    public IReadOnlyList<ScheduleBlock> StudentBlocks => Blocks.Where(block => block.IsStudentTime).ToArray();
    public TimeOnly? FirstBell => StudentBlocks.Count > 0 ? StudentBlocks.Min(block => block.Start) : null;
    public TimeOnly? LastBell => StudentBlocks.Count > 0 ? StudentBlocks.Max(block => block.End) : null;
}

public sealed record BellSchedule(SchoolLevel Level, string SchoolName, string SourceYear, IReadOnlyList<DailyBellSchedule> Days)
{
    public DailyBellSchedule? For(DayOfWeek day) => Days.FirstOrDefault(schedule => schedule.Days.Contains(day));
}

public sealed record Student(string Name, SchoolLevel Level, string Color);

public enum BlockDayState
{
    /// <summary>No blocks run today — a weekend, a break, or a holiday.</summary>
    NoSchoolToday,
    BeforeSchool,
    InBlock,
    /// <summary>In a passing period between two blocks.</summary>
    BetweenBlocks,
    AfterSchool
}

/// <param name="FractionThroughDay">0 at the first bell, 1 at the last — drives the day progress bar.</param>
public sealed record BlockProgress(
    BlockDayState State,
    ScheduleBlock? Current,
    ScheduleBlock? Next,
    double FractionThroughBlock,
    double FractionThroughDay,
    TimeSpan RemainingInBlock,
    string? Note = null)
{
    public bool IsInSchool => State is BlockDayState.InBlock or BlockDayState.BetweenBlocks;

    public string Headline => State switch
    {
        BlockDayState.NoSchoolToday => Note ?? "No school today",
        BlockDayState.BeforeSchool => Next is null ? "No school today" : $"Starts {Next.Start:h:mm tt}",
        BlockDayState.InBlock => Current?.Name ?? string.Empty,
        BlockDayState.BetweenBlocks => Next is null ? "Passing period" : $"Passing → {Next.Name}",
        _ => "Day complete"
    };

    public string Detail => State switch
    {
        BlockDayState.InBlock when Current is not null =>
            $"{Current.TimeText} • {FormatRemaining(RemainingInBlock)} left",
        BlockDayState.BetweenBlocks when Next is not null =>
            $"{Next.Name} at {Next.Start:h:mm tt}",
        BlockDayState.BeforeSchool when Next is not null => Next.Name,
        _ => string.Empty
    };

    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalMinutes >= 1
        ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} min"
        : "less than a minute";
}
