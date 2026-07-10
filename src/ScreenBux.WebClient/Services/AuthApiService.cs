using System.Net.Http.Json;
using ScreenBux.Shared.Models.Auth;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Calls the WebServer account endpoints and updates auth state on success.
/// </summary>
public class AuthApiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenProvider _tokenProvider;
    private readonly TokenAuthenticationStateProvider _authStateProvider;

    public AuthApiService(
        HttpClient httpClient,
        TokenProvider tokenProvider,
        TokenAuthenticationStateProvider authStateProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _authStateProvider = authStateProvider;
    }

    public Task<string?> RegisterAsync(string email, string password) =>
        SendAsync("api/account/register", new RegisterRequest { Email = email, Password = password });

    public Task<string?> LoginAsync(string email, string password) =>
        SendAsync("api/account/login", new LoginRequest { Email = email, Password = password });

    public void Logout()
    {
        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    private async Task<string?> SendAsync(string url, object payload)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(url, payload);
        }
        catch (Exception ex)
        {
            return $"Could not reach the server: {ex.Message}";
        }

        if (!response.IsSuccessStatusCode)
        {
            return $"Request failed ({(int)response.StatusCode}).";
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth is null || string.IsNullOrEmpty(auth.Token))
        {
            return "Server returned an invalid response.";
        }

        _tokenProvider.Token = auth.Token;
        _tokenProvider.Email = auth.Email;
        _authStateProvider.NotifyAuthenticationStateChanged();
        return null; // success
    }
}
