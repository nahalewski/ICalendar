using FamilyHub.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Infrastructure;

public sealed record BellScheduleBook(IReadOnlyList<BellSchedule> Schedules, IReadOnlyList<Student> Students)
{
    public BellSchedule? For(SchoolLevel level) => Schedules.FirstOrDefault(schedule => schedule.Level == level);
    public static BellScheduleBook Empty { get; } = new([], []);
}

public sealed class JsonBellScheduleProvider(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public BellScheduleBook Load()
    {
        try
        {
            if (!File.Exists(path)) return BellScheduleBook.Empty;
            var file = JsonSerializer.Deserialize<ScheduleFile>(File.ReadAllText(path), JsonOptions);
            if (file is null) return BellScheduleBook.Empty;

            var schedules = (file.Schedules ?? [])
                .Where(schedule => schedule is not null)
                .Select(schedule => new BellSchedule(schedule.Level, schedule.SchoolName ?? string.Empty,
                    schedule.SourceYear ?? string.Empty,
                    (schedule.Days ?? []).Select(day => new DailyBellSchedule(
                        day.Name ?? string.Empty,
                        day.Days ?? [],
                        (day.Blocks ?? [])
                            .Select(block => new ScheduleBlock(block.Name, block.Start, block.End, block.Kind))
                            .OrderBy(block => block.Start).ToArray())).ToArray()))
                .ToArray();

            var students = (file.Students ?? [])
                .Where(student => student is not null && !string.IsNullOrWhiteSpace(student.Name))
                .Select(student => new Student(student.Name, student.Level,
                    string.IsNullOrWhiteSpace(student.Color) ? "#2563EB" : student.Color))
                .ToArray();

            return new BellScheduleBook(schedules, students);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return BellScheduleBook.Empty;
        }
    }

    private sealed record ScheduleFile(ScheduleEntry[]? Schedules, StudentEntry[]? Students);
    private sealed record ScheduleEntry(SchoolLevel Level, string? SchoolName, string? SourceYear, DayEntry[]? Days);
    private sealed record DayEntry(string? Name, DayOfWeek[]? Days, BlockEntry[]? Blocks);
    private sealed record BlockEntry(string Name, TimeOnly Start, TimeOnly End, BlockKind Kind = BlockKind.Class);
    private sealed record StudentEntry(string Name, SchoolLevel Level, string? Color);
}
