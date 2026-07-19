using FamilyHub.Contracts;
using FamilyHub.Core;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Infrastructure;

public sealed class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyHub", "weather-cache.json");

    public async Task<(CurrentWeather Current, IReadOnlyList<DailyForecast> Forecast)> GetAsync(
        double latitude, double longitude, CancellationToken cancellationToken)
    {
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,dew_point_2m,precipitation,weather_code,cloud_cover,pressure_msl,visibility,wind_speed_10m,wind_gusts_10m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,wind_speed_10m_max,wind_gusts_10m_max,relative_humidity_2m_mean,uv_index_max,sunrise,sunset&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=auto&forecast_days=7");
        try
        {
            var response = await httpClient.GetFromJsonAsync<OpenMeteoResponse>(url, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The weather provider returned an empty response.");
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(_cachePath, JsonSerializer.Serialize(response, JsonOptions), cancellationToken);
            return Map(response, false, DateTimeOffset.Now, latitude, longitude);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (!File.Exists(_cachePath)) throw;
            var cached = JsonSerializer.Deserialize<OpenMeteoResponse>(await File.ReadAllTextAsync(_cachePath, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("The cached weather data is unreadable.");
            return Map(cached, true, File.GetLastWriteTimeUtc(_cachePath), latitude, longitude);
        }
    }

    public async Task<WeatherLocation?> SearchLocationAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (LocationQuery.TryParseCoordinates(query, out var latitude, out var longitude))
            return new WeatherLocation(FormattableString.Invariant($"{latitude:0.####}, {longitude:0.####}"), latitude, longitude);

        // "Sanford, NC" must be sent as "Sanford"; the state is applied afterwards as a ranking hint.
        var (searchTerm, region) = LocationQuery.Split(query);
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(searchTerm)}&count={LocationQuery.CandidateCount}&language=en&format=json";
        var response = await httpClient.GetFromJsonAsync<GeocodingResponse>(url, JsonOptions, cancellationToken);
        if (response?.Results is not { Length: > 0 } results) return null;
        var result = results
            .OrderByDescending(candidate => LocationQuery.Score(searchTerm, region, candidate.Name, candidate.Admin1,
                candidate.CountryCode, candidate.Country, candidate.Postcodes))
            .First();
        return new WeatherLocation(LocationQuery.Describe(result.Name, result.Admin1, result.CountryCode), result.Latitude, result.Longitude);
    }

    private static (CurrentWeather, IReadOnlyList<DailyForecast>) Map(OpenMeteoResponse response, bool cached, DateTimeOffset updatedAt,
        double latitude, double longitude)
    {
        // Reports the coordinates actually queried; the display name is owned by the caller's saved location.
        var location = FormattableString.Invariant($"{latitude:0.####}, {longitude:0.####}");
        var current = new CurrentWeather(location, response.Current.Temperature, response.Current.ApparentTemperature,
            Describe(response.Current.WeatherCode), response.Current.WeatherCode, response.Current.Humidity,
            response.Current.WindSpeed, updatedAt, cached, response.Current.DewPoint, response.Current.WindGust,
            response.Current.Pressure, response.Current.Visibility / 1609.344, response.Current.CloudCover, response.Current.Precipitation);
        // Open-Meteo omits variables it cannot supply, so the daily arrays are not guaranteed to be
        // the same length. Take only the prefix every array covers rather than indexing past the end.
        var daily = response.Daily;
        var count = new[] { 7, daily.Time.Length, daily.WeatherCode.Length, daily.High.Length, daily.Low.Length,
            daily.PrecipitationProbability.Length, daily.Precipitation.Length, daily.MaxWind.Length,
            daily.Gust.Length, daily.Humidity.Length, daily.Uv.Length, daily.Sunrise.Length, daily.Sunset.Length }.Min();
        var days = Enumerable.Range(0, count).Select(index => new DailyForecast(
            DateOnly.Parse(response.Daily.Time[index], CultureInfo.InvariantCulture), response.Daily.WeatherCode[index],
            Describe(response.Daily.WeatherCode[index]), response.Daily.High[index], response.Daily.Low[index],
            response.Daily.PrecipitationProbability[index], response.Daily.MaxWind[index], response.Daily.Precipitation[index],
            response.Daily.Gust[index], response.Daily.Humidity[index], response.Daily.Uv[index],
            TimePart(response.Daily.Sunrise[index]), TimePart(response.Daily.Sunset[index]))).ToArray();
        return (current, days);
    }

    private static string TimePart(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
        ? parsed.ToString("h:mm tt", CultureInfo.CurrentCulture) : value;
    private static string Describe(int code) => code switch { 0 => "Clear", 1 or 2 => "Partly cloudy", 3 => "Cloudy", 45 or 48 => "Fog", 51 or 53 or 55 or 61 or 63 or 65 => "Rain", 71 or 73 or 75 => "Snow", 80 or 81 or 82 => "Showers", 95 or 96 or 99 => "Thunderstorms", _ => "Mixed conditions" };

    private sealed record OpenMeteoResponse([property: JsonPropertyName("current")] CurrentData Current, [property: JsonPropertyName("daily")] DailyData Daily);
    private sealed record GeocodingResponse([property: JsonPropertyName("results")] GeocodingResult[]? Results);
    private sealed record GeocodingResult([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("admin1")] string? Admin1,
        [property: JsonPropertyName("country_code")] string? CountryCode, [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude, [property: JsonPropertyName("country")] string? Country = null,
        [property: JsonPropertyName("postcodes")] string[]? Postcodes = null);
    private sealed record CurrentData(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("relative_humidity_2m")] int Humidity,
        [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
        [property: JsonPropertyName("dew_point_2m")] double DewPoint,
        [property: JsonPropertyName("precipitation")] double Precipitation,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("cloud_cover")] int CloudCover,
        [property: JsonPropertyName("pressure_msl")] double Pressure,
        [property: JsonPropertyName("visibility")] double Visibility,
        [property: JsonPropertyName("wind_speed_10m")] double WindSpeed,
        [property: JsonPropertyName("wind_gusts_10m")] double WindGust);
    private sealed record DailyData(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int[] WeatherCode,
        [property: JsonPropertyName("temperature_2m_max")] double[] High,
        [property: JsonPropertyName("temperature_2m_min")] double[] Low,
        [property: JsonPropertyName("precipitation_probability_max")] int[] PrecipitationProbability,
        [property: JsonPropertyName("precipitation_sum")] double[] Precipitation,
        [property: JsonPropertyName("wind_speed_10m_max")] double[] MaxWind,
        [property: JsonPropertyName("wind_gusts_10m_max")] double[] Gust,
        [property: JsonPropertyName("relative_humidity_2m_mean")] int[] Humidity,
        [property: JsonPropertyName("uv_index_max")] double[] Uv,
        [property: JsonPropertyName("sunrise")] string[] Sunrise,
        [property: JsonPropertyName("sunset")] string[] Sunset);
}
