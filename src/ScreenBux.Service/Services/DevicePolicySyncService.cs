using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Models.Devices;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Redeems a parent-provided link code (from configuration) to bind this device to an account,
/// then periodically fetches this device's policy from the server and writes it to the local
/// JSON cache consumed by <see cref="PolicyService"/>.
/// </summary>
public class DevicePolicySyncService : BackgroundService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ILogger<DevicePolicySyncService> _logger;
    private readonly IConfiguration _configuration;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _policyFilePath;

    public DevicePolicySyncService(
        ILogger<DevicePolicySyncService> logger,
        IConfiguration configuration,
        DeviceIdentityService deviceIdentity,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _deviceIdentity = deviceIdentity;
        _httpClientFactory = httpClientFactory;
        _policyFilePath = configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            _logger.LogInformation("ServerBaseUrl not configured; device policy sync disabled.");
            return;
        }

        var intervalSeconds = int.TryParse(_configuration["DevicePolicySyncIntervalSeconds"], out var s) ? s : 60;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureLinkedAsync(serverBaseUrl, stoppingToken);
                await FetchPolicyAsync(serverBaseUrl, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device policy sync iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    private async Task EnsureLinkedAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var state = _deviceIdentity.GetOrCreate();
        if (state.IsLinked)
        {
            return;
        }

        var linkCode = _configuration["LinkCode"];
        if (string.IsNullOrWhiteSpace(linkCode))
        {
            _logger.LogInformation("Device not linked and no LinkCode configured; waiting.");
            return;
        }

        var (success, _, _) = await RedeemCodeAsync(linkCode, cancellationToken);
        if (!success)
        {
            _logger.LogWarning("Auto-redemption of configured LinkCode failed.");
        }
    }

    /// <summary>
    /// Redeems a parent-generated link code with the server.
    /// Returns (success, message, deviceId).
    /// </summary>
    public async Task<(bool Success, string Message, Guid? DeviceId)> RedeemCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            return (false, "ServerBaseUrl is not configured.", null);
        }

        var state = _deviceIdentity.GetOrCreate();
        var client = CreateClient(serverBaseUrl);
        var request = new RedeemLinkCodeRequest
        {
            Code = code.Trim().ToUpperInvariant(),
            DeviceName = Environment.MachineName,
            MachineKey = state.MachineKey
        };

        try
        {
            using var response = await client.PostAsJsonAsync("api/devices/redeem", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Server rejected the code ({(int)response.StatusCode}).", null);
            }

            var result = await response.Content.ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken);
            if (result is null || string.IsNullOrEmpty(result.Token))
            {
                return (false, "Server returned an invalid response.", null);
            }

            state.DeviceId = result.DeviceId;
            state.DeviceToken = result.Token;
            _deviceIdentity.Save(state);
            _logger.LogInformation("Device linked successfully as {DeviceId}.", state.DeviceId);
            return (true, $"Device linked as {result.DeviceId}.", result.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redeeming link code.");
            return (false, $"Error: {ex.Message}", null);
        }
    }

    private async Task FetchPolicyAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var state = _deviceIdentity.GetOrCreate();
        if (!state.IsLinked)
        {
            return;
        }

        var client = CreateClient(serverBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", state.DeviceToken);

        using var response = await client.GetAsync($"api/devices/{state.DeviceId}/policy", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Device token rejected; clearing link state to re-link.");
            state.DeviceToken = null;
            _deviceIdentity.Save(state);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Policy fetch failed ({Status}).", (int)response.StatusCode);
            return;
        }

        var policy = await response.Content.ReadFromJsonAsync<PolicyConfiguration>(cancellationToken);
        if (policy is null)
        {
            return;
        }

        PolicyStorage.EnsurePolicyDirectory(_policyFilePath);
        await File.WriteAllTextAsync(_policyFilePath, JsonSerializer.Serialize(policy, WriteOptions), cancellationToken);
        _logger.LogInformation("Updated local policy cache from server for device {DeviceId}.", state.DeviceId);
    }

    private HttpClient CreateClient(string serverBaseUrl)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/");
        return client;
    }
}
