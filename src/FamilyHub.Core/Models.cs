namespace FamilyHub.Core;

public enum DashboardPage { Daily, Weekly, Monthly, Weather, Agenda, Family, Settings, ClassBlock, News }
public enum TransitionKind { Fade, Slide, Crossfade }

public sealed record PersonProfile(
    Guid Id, string DisplayName, IReadOnlyList<string> Aliases, string Color,
    string Initials, int Priority = 0, bool IsEnabled = true);

public sealed record CalendarEvent(
    string ProviderId, string CalendarId, string Title, DateTimeOffset Start,
    DateTimeOffset End, bool IsAllDay, string? Location, IReadOnlyList<Guid> PersonIds,
    string Category, string Color);

public sealed record CurrentWeather(
    string Location, double Temperature, double FeelsLike, string Condition,
    int WeatherCode, int Humidity, double WindSpeed, DateTimeOffset UpdatedAt,
    bool IsCached = false, double DewPoint = 0, double WindGust = 0, double Pressure = 0,
    double VisibilityMiles = 0, int CloudCover = 0, double Precipitation = 0);

public sealed record DailyForecast(
    DateOnly Date, int WeatherCode, string Condition, double High, double Low,
    int PrecipitationProbability, double MaxWindSpeed, double PrecipitationAmount = 0,
    double WindGust = 0, int Humidity = 0, double UvIndex = 0, string Sunrise = "", string Sunset = "");

public static class SeedPeople
{
    public static IReadOnlyList<PersonProfile> Create() =>
    [
        Person("Dad", "#2563EB", "Dad", "Brandon", "Greg"),
        Person("Xander", "#F97316", "Xander", "Buddy"),
        Person("Jada", "#DB2777", "Jada", "Mom"),
        Person("Allie", "#7C3AED", "Allie"),
        Person("Gabby", "#0891B2", "Gabby"),
        Person("Elliana", "#16A34A", "Elliana", "Boo-Boo", "Boo Boo", "Booboo"),
        Person("Lilly", "#CA8A04", "Lilly", "Lily"),
        Person("Sherry", "#DC2626", "Sherry", "Mawmaw", "Maw Maw", "Grandma"),
        Person("Benji", "#475569", "Benji", "Ben", "Uncle", "Uncle Ben")
    ];

    private static PersonProfile Person(string name, string color, params string[] aliases) =>
        new(Guid.NewGuid(), name, aliases, color, name[..1].ToUpperInvariant());
}
