using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.WebClient.Services;

public class PolicyApiService
{
    private readonly HttpClient _httpClient;

    public PolicyApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<string> GetPolicyJsonAsync()
    {
        using var response = await _httpClient.GetAsync("api/policy");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task UpdatePolicyAsync(PolicyConfiguration policy)
    {
        var response = await _httpClient.PutAsJsonAsync("api/policy", policy, PolicyJsonOptions.Default);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<PolicyProfileDto>> GetProfilesAsync()
    {
        var profiles = await _httpClient.GetFromJsonAsync<List<PolicyProfileDto>>("api/policy/profiles", PolicyJsonOptions.Default);
        return profiles ?? new List<PolicyProfileDto>();
    }

    public async Task<PolicyProfileDto?> UpdateProfileAsync(Guid profileId, string name, PolicyConfiguration policy)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/policy/profiles/{profileId}", new PolicyProfileDto
        {
            Name = name,
            Policy = policy
        }, PolicyJsonOptions.Default);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PolicyProfileDto>(PolicyJsonOptions.Default);
    }

    public async Task ActivateProfileAsync(Guid profileId)
    {
        var response = await _httpClient.PostAsync($"api/policy/profiles/{profileId}/activate", null);
        response.EnsureSuccessStatusCode();
    }
}
