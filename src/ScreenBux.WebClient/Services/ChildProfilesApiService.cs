using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models.Devices;

namespace ScreenBux.WebClient.Services;

/// <summary>Calls the WebServer child-profile endpoints (list, create, rename, delete).</summary>
public class ChildProfilesApiService
{
    private readonly HttpClient _httpClient;

    public ChildProfilesApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<IReadOnlyList<ChildProfileDto>> ListProfilesAsync()
    {
        var profiles = await _httpClient.GetFromJsonAsync<List<ChildProfileDto>>("api/childprofiles");
        return profiles ?? new List<ChildProfileDto>();
    }

    public async Task<ChildProfileDto?> CreateProfileAsync(string displayName)
    {
        var response = await _httpClient.PostAsJsonAsync("api/childprofiles", new CreateChildProfileRequest { DisplayName = displayName });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChildProfileDto>();
    }

    public async Task<ChildProfileDto?> RenameProfileAsync(Guid id, string displayName)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/childprofiles/{id}", new UpdateChildProfileRequest { DisplayName = displayName });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChildProfileDto>();
    }

    public async Task<bool> DeleteProfileAsync(Guid id)
    {
        var response = await _httpClient.DeleteAsync($"api/childprofiles/{id}");
        return response.IsSuccessStatusCode;
    }
}
