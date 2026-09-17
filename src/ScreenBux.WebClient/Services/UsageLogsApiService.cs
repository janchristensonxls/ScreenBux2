using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Calls the WebServer's usage-logs endpoint to download a completed on-demand fetch once
/// notified (via <see cref="MonitoringService.UsageLogsReady"/>) that it's ready.
/// </summary>
public class UsageLogsApiService
{
    private readonly HttpClient _httpClient;

    public UsageLogsApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<IReadOnlyList<UsageDayLog>> GetAsync(Guid requestId)
    {
        var days = await _httpClient.GetFromJsonAsync<List<UsageDayLog>>($"api/usagelogs/{requestId}");
        return days ?? new List<UsageDayLog>();
    }
}
