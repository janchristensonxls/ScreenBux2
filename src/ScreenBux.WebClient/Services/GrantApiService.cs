using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Calls the WebServer device-grant endpoints (get/set/add minutes).
/// </summary>
public class GrantApiService
{
    private readonly HttpClient _httpClient;

    public GrantApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<GrantDto?> GetGrantAsync(Guid deviceId)
    {
        return await _httpClient.GetFromJsonAsync<GrantDto>($"api/devices/{deviceId}/grant");
    }

    public async Task<GrantDto?> SetGrantAsync(Guid deviceId, DateTime? expiresAtUtc)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/devices/{deviceId}/grant", new { ExpiresAtUtc = expiresAtUtc });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GrantDto>();
    }

    public async Task<GrantDto?> ClearGrantAsync(Guid deviceId) => await SetGrantAsync(deviceId, null);

    public async Task<GrantDto?> AddMinutesAsync(Guid deviceId, int minutes)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/devices/{deviceId}/grant/add", new { Minutes = minutes });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GrantDto>();
    }
}
