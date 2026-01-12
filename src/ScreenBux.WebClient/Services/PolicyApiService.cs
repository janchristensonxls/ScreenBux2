using System.Net.Http.Json;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebClient.Services;

public class PolicyApiService
{
    private readonly HttpClient _httpClient;

    public PolicyApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetPolicyJsonAsync()
    {
        using var response = await _httpClient.GetAsync("api/policy");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task UpdatePolicyAsync(PolicyConfiguration policy)
    {
        var response = await _httpClient.PutAsJsonAsync("api/policy", policy);
        response.EnsureSuccessStatusCode();
    }
}
