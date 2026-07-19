using FamilyHub.Core;

namespace FamilyHub.Application;

/// <summary>
/// Works out where a child is in their school day. Kept free of UI and of the system clock so the
/// block-by-block behaviour is deterministic and unit-testable.
/// </summary>
public static class BellScheduleService
{
    /// <param name="noSchoolReason">Set when the school calendar marks the day a break or holiday.</param>
    public static BlockProgress Evaluate(BellSchedule? schedule, DateTimeOffset now, string? noSchoolReason = null)
    {
        if (noSchoolReason is not null)
            return new BlockProgress(BlockDayState.NoSchoolToday, null, null, 0, 0, TimeSpan.Zero, noSchoolReason);

        // Both the weekday and the time must come from the same clock, or a machine whose offset
        // differs from the supplied one would look up Tuesday's blocks against Monday's time.
        var local = now.LocalDateTime;
        var day = schedule?.For(local.DayOfWeek);
        var blocks = day?.StudentBlocks;
        if (blocks is null || blocks.Count == 0)
            return new BlockProgress(BlockDayState.NoSchoolToday, null, null, 0, 0, TimeSpan.Zero,
                schedule is null ? "No schedule configured" : "No school today");

        var ordered = blocks.OrderBy(block => block.Start).ToArray();
        var time = TimeOnly.FromDateTime(local);
        var firstBell = ordered[0].Start;
        var lastBell = ordered[^1].End;
        var throughDay = Fraction(firstBell, lastBell, time);

        if (time < firstBell)
            return new BlockProgress(BlockDayState.BeforeSchool, null, ordered[0], 0, 0, TimeSpan.Zero);

        if (time >= lastBell)
            return new BlockProgress(BlockDayState.AfterSchool, null, null, 1, 1, TimeSpan.Zero);

        var current = Array.Find(ordered, block => block.Contains(time));
        if (current is not null)
        {
            var next = ordered.FirstOrDefault(block => block.Start >= current.End);
            return new BlockProgress(BlockDayState.InBlock, current, next,
                Fraction(current.Start, current.End, time), throughDay, current.End - time);
        }

        // Between blocks: the passing period after the block that just ended.
        var upcoming = ordered.First(block => block.Start > time);
        return new BlockProgress(BlockDayState.BetweenBlocks, null, upcoming, 0, throughDay, upcoming.Start - time);
    }

    private static double Fraction(TimeOnly start, TimeOnly end, TimeOnly now)
    {
        var span = (end - start).TotalSeconds;
        if (span <= 0) return 0;
        return Math.Clamp((now - start).TotalSeconds / span, 0, 1);
    }
}
