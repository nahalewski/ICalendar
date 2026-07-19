using System.Net.Http.Json;
using FamilyHub.Contracts;

namespace FamilyHub.Mobile.Services;

public sealed class FamilyHubClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private Uri _baseAddress = new("http://familyhub.local:5643/");
    public Uri BaseAddress { get => _baseAddress; set => _baseAddress = new Uri(value.ToString().TrimEnd('/') + "/"); }
    private Uri Endpoint(string path) => new(_baseAddress, path);
    public Task<DashboardControlDto?> GetDashboardAsync(CancellationToken token) => _http.GetFromJsonAsync<DashboardControlDto>(Endpoint("api/dashboard"), token);
    public async Task<DashboardControlDto?> SetScreenAsync(string screen, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(Endpoint("api/control/screen"), new ScreenCommandDto(screen), token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DashboardControlDto>(token);
    }
    public async Task SaveRotationAsync(RotationSettingsDto settings, CancellationToken token)
    {
        using var response = await _http.PutAsJsonAsync(Endpoint("api/settings/rotation"), settings, token);
        response.EnsureSuccessStatusCode();
    }
    public async Task SaveEventAsync(CompanionEventDto item, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(Endpoint("api/events"), item, token);
        response.EnsureSuccessStatusCode();
    }
    public Task<GoogleCalendarStatusDto?> GetGoogleStatusAsync(CancellationToken token) => _http.GetFromJsonAsync<GoogleCalendarStatusDto>(Endpoint("api/google/status"), token);
    public void Dispose() => _http.Dispose();
}
