using FamilyHub.Core;

namespace FamilyHub.Infrastructure;

public interface IWeatherProvider
{
    Task<(CurrentWeather Current, IReadOnlyList<DailyForecast> Forecast)> GetAsync(
        double latitude, double longitude, CancellationToken cancellationToken);
    Task<WeatherLocation?> SearchLocationAsync(string query, CancellationToken cancellationToken);
}

public sealed record WeatherLocation(string Name, double Latitude, double Longitude);

public interface ISchoolCalendarProvider
{
    Task<IReadOnlyList<CalendarEvent>> ImportAsync(Stream source, CancellationToken cancellationToken);
}

public interface ITokenStore
{
    Task StoreAsync(string accountId, ReadOnlyMemory<byte> token, CancellationToken cancellationToken);
    Task<ReadOnlyMemory<byte>?> RetrieveAsync(string accountId, CancellationToken cancellationToken);
}
