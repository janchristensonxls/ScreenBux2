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

    /// <summary>Removes a child's own mode so it follows the default mode again.</summary>
    public async Task FollowDefaultAsync(Guid childProfileId)
    {
        var response = await _httpClient.DeleteAsync($"api/policy/children/{childProfileId}/assignment");
        response.EnsureSuccessStatusCode();
    }

    public async Task<PolicyAssignmentsDto> GetAssignmentsAsync()
    {
        return await _httpClient.GetFromJsonAsync<PolicyAssignmentsDto>("api/policy/assignments", PolicyJsonOptions.Default)
               ?? new PolicyAssignmentsDto();
    }

    // --- Mode schedule / temporary switch (docs/decisions/mode-scheduling-and-temporary-overrides.md) ---

    /// <summary>Configured and current mode for the account default (first) and every child.</summary>
    public async Task<List<ModeScopeStatusDto>> GetModesAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<ModeScopeStatusDto>>("api/policy/modes", PolicyJsonOptions.Default)
               ?? new List<ModeScopeStatusDto>();
    }

    /// <summary>Temporarily switches one child, or (childProfileId null) everyone, to a mode.</summary>
    public async Task SetOverrideAsync(Guid? childProfileId, SetModeOverrideRequest request)
    {
        var url = childProfileId is Guid childId ? $"api/policy/override?childProfileId={childId}" : "api/policy/override/everyone";
        var response = await _httpClient.PostAsJsonAsync(url, request, PolicyJsonOptions.Default);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Ends a temporary switch of one child, or (childProfileId null) of the account.</summary>
    public async Task ClearOverrideAsync(Guid? childProfileId)
    {
        var response = await _httpClient.DeleteAsync($"api/policy/override{ChildQuery(childProfileId)}");
        await EnsureSuccessAsync(response);
    }

    /// <summary>
    /// Saves a schedule for one child or the account. For a child, null means "follow the account
    /// schedule" and an empty list means "no schedule".
    /// </summary>
    public async Task SetScheduleAsync(Guid? childProfileId, List<ModeScheduleEntry>? schedule)
    {
        var response = schedule is null
            ? await _httpClient.DeleteAsync($"api/policy/schedule{ChildQuery(childProfileId)}")
            : await _httpClient.PutAsJsonAsync($"api/policy/schedule{ChildQuery(childProfileId)}", schedule, PolicyJsonOptions.Default);
        await EnsureSuccessAsync(response);
    }

    private static string ChildQuery(Guid? childProfileId) =>
        childProfileId is Guid childId ? $"?childProfileId={childId}" : string.Empty;

    /// <summary>Like EnsureSuccessStatusCode, but surfaces the server's validation message.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
            {
                throw new HttpRequestException(text, null, response.StatusCode);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON - fall through to the generic error.
        }

        response.EnsureSuccessStatusCode();
    }
}
