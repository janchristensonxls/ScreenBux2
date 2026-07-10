using System.Net.Http.Json;
using ScreenBux.Shared.Models.Devices;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Calls the WebServer device endpoints (generate link code, list devices).
/// </summary>
public class DevicesApiService
{
    private readonly HttpClient _httpClient;

    public DevicesApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LinkCodeResponse?> GenerateLinkCodeAsync()
    {
        var response = await _httpClient.PostAsync("api/devices/linkcode", content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LinkCodeResponse>();
    }

    public async Task<IReadOnlyList<DeviceDto>> ListDevicesAsync()
    {
        var devices = await _httpClient.GetFromJsonAsync<List<DeviceDto>>("api/devices");
        return devices ?? new List<DeviceDto>();
    }
}
