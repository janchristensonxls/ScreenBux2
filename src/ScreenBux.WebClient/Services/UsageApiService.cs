using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebClient.Services;

/// <summary>Calls the WebServer usage endpoints (today's summary, short history) for stats views.</summary>
public class UsageApiService
{
    private readonly HttpClient _httpClient;

    public UsageApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<UsageSummaryDto?> GetSummaryAsync(Guid childProfileId, DateOnly? effectiveDate = null)
    {
        var url = $"api/usage/{childProfileId}";
        if (effectiveDate is not null)
        {
            url += $"?effectiveDate={effectiveDate:yyyy-MM-dd}";
        }

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<UsageSummaryDto>();
    }

    public async Task<List<UsageSummaryDto>> GetHistoryAsync(Guid childProfileId, int days = 7, DateOnly? endDate = null)
    {
        var url = $"api/usage/{childProfileId}/history?days={days}";
        if (endDate is not null)
        {
            url += $"&endDate={endDate:yyyy-MM-dd}";
        }

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return new List<UsageSummaryDto>();
        }

        var history = await response.Content.ReadFromJsonAsync<List<UsageSummaryDto>>();
        return history ?? new List<UsageSummaryDto>();
    }
}
