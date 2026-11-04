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

    public async Task<PolicyProfileDto?> CreateProfileAsync(string name, PolicyConfiguration policy)
    {
        var response = await _httpClient.PostAsJsonAsync("api/policy/profiles", new PolicyProfileDto
        {
            Name = name,
            Policy = policy
        }, PolicyJsonOptions.Default);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PolicyProfileDto>(PolicyJsonOptions.Default);
    }

    public async Task DeleteProfileAsync(Guid profileId)
    {
        var response = await _httpClient.DeleteAsync($"api/policy/profiles/{profileId}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Activates a mode for one child, or (childProfileId null) as the account default.</summary>
    public async Task ActivateProfileAsync(Guid profileId, Guid? childProfileId = null)
    {
        var url = childProfileId is Guid childId
            ? $"api/policy/profiles/{profileId}/activate?childProfileId={childId}"
            : $"api/policy/profiles/{profileId}/activate";
        var response = await _httpClient.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Makes a mode the default and puts every child on it.</summary>
    public async Task ActivateProfileForEveryoneAsync(Guid profileId)
    {
        var response = await _httpClient.PostAsync($"api/policy/profiles/{profileId}/activate-all", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<PolicyAssignmentsDto> GetAssignmentsAsync()
    {
        return await _httpClient.GetFromJsonAsync<PolicyAssignmentsDto>("api/policy/assignments", PolicyJsonOptions.Default)
               ?? new PolicyAssignmentsDto();
    }
}
