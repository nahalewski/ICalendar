using FamilyHub.Contracts;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Mobile.Services;

public sealed class OpenMeteoMobileService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public async Task<MobileWeatherSnapshot> GetAsync(double latitude, double longitude, CancellationToken token)
    {
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&temperature_unit=fahrenheit&timezone=auto&forecast_days=7");
        var data = await _http.GetFromJsonAsync<Response>(url, token) ?? throw new InvalidDataException("Weather response was empty.");
        var daily = data.Daily;
        var count = new[] { 7, daily.Time.Length, daily.Codes.Length, daily.Highs.Length, daily.Lows.Length, daily.Rain.Length }.Min();
        var days = Enumerable.Range(0, count).Select(index => new MobileWeatherDay(
            DateOnly.Parse(daily.Time[index], CultureInfo.InvariantCulture), Describe(daily.Codes[index]),
            (int)Math.Round(daily.Highs[index]), (int)Math.Round(daily.Lows[index]), daily.Rain[index],
            WeatherGlyph.For(daily.Codes[index]))).ToArray();
        return new MobileWeatherSnapshot((int)Math.Round(data.Current.Temperature), (int)Math.Round(data.Current.FeelsLike),
            Describe(data.Current.Code), days, WeatherGlyph.For(data.Current.Code));
    }
    public async Task<MobileWeatherLocation?> SearchLocationAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (LocationQuery.TryParseCoordinates(query, out var latitude, out var longitude))
            return new MobileWeatherLocation(FormattableString.Invariant($"{latitude:0.####}, {longitude:0.####}"), latitude, longitude);

        // "Sanford, NC" must be sent as "Sanford"; the state is applied afterwards as a ranking hint.
        var (searchTerm, region) = LocationQuery.Split(query);
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(searchTerm)}&count={LocationQuery.CandidateCount}&language=en&format=json";
        var response = await _http.GetFromJsonAsync<GeocodingResponse>(url, token);
        if (response?.Results is not { Length: > 0 } results) return null;
        var result = results
            .OrderByDescending(candidate => LocationQuery.Score(searchTerm, region, candidate.Name, candidate.Admin1,
                candidate.CountryCode, candidate.Country, candidate.Postcodes))
            .First();
        return new MobileWeatherLocation(LocationQuery.Describe(result.Name, result.Admin1, result.CountryCode), result.Latitude, result.Longitude);
    }
    private static string Describe(int code) => code switch { 0 => "Clear", 1 or 2 => "Partly cloudy", 3 => "Cloudy", 45 or 48 => "Fog", 51 or 53 or 55 or 61 or 63 or 65 => "Rain", 71 or 73 or 75 => "Snow", 80 or 81 or 82 => "Showers", 95 or 96 or 99 => "Thunderstorms", _ => "Mixed conditions" };
    public void Dispose() => _http.Dispose();
    private sealed record Response([property: JsonPropertyName("current")] CurrentData Current, [property: JsonPropertyName("daily")] DailyData Daily);
    private sealed record CurrentData([property: JsonPropertyName("temperature_2m")] double Temperature, [property: JsonPropertyName("apparent_temperature")] double FeelsLike, [property: JsonPropertyName("weather_code")] int Code);
    private sealed record DailyData([property: JsonPropertyName("time")] string[] Time, [property: JsonPropertyName("weather_code")] int[] Codes, [property: JsonPropertyName("temperature_2m_max")] double[] Highs, [property: JsonPropertyName("temperature_2m_min")] double[] Lows, [property: JsonPropertyName("precipitation_probability_max")] int[] Rain);
    private sealed record GeocodingResponse([property: JsonPropertyName("results")] GeocodingResult[]? Results);
    private sealed record GeocodingResult([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("admin1")] string? Admin1, [property: JsonPropertyName("country_code")] string? CountryCode, [property: JsonPropertyName("latitude")] double Latitude, [property: JsonPropertyName("longitude")] double Longitude, [property: JsonPropertyName("country")] string? Country = null, [property: JsonPropertyName("postcodes")] string[]? Postcodes = null);
}
public sealed record MobileWeatherSnapshot(int Temperature, int FeelsLike, string Condition, IReadOnlyList<MobileWeatherDay> Days, string Icon = "🌤");
public sealed record MobileWeatherDay(DateOnly Date, string Condition, int High, int Low, int RainChance, string Icon = "🌤");
public sealed record MobileWeatherLocation(string Name, double Latitude, double Longitude);
