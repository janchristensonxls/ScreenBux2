using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Service.Services;

/// <summary>
/// Relays a notification acknowledgement from the Agent to the WebServer, which broadcasts it to
/// the parent's WebClient session over SignalR. Best-effort/fire-and-forget - a failed relay just
/// means the parent doesn't see the acknowledgement live; it never blocks the Agent's poll loop.
/// Follows the same device-token bearer auth pattern as <see cref="UsageTrackingService"/>.
/// </summary>
public class NotificationRelayService
{
    private readonly IConfiguration _configuration;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationRelayService> _logger;

    public NotificationRelayService(
        IConfiguration configuration,
        DeviceIdentityService deviceIdentity,
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationRelayService> logger)
    {
        _configuration = configuration;
        _deviceIdentity = deviceIdentity;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RelayAckAsync(NotificationAckMessage ack, CancellationToken cancellationToken = default)
    {
        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            return;
        }

        var state = _deviceIdentity.GetOrCreate();
        if (!state.IsLinked)
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", state.DeviceToken);

            using var response = await client.PostAsJsonAsync("api/devices/notifications/ack", ack, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to relay notification ack {NotificationId}: {StatusCode}",
                    ack.NotificationId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error relaying notification ack {NotificationId}", ack.NotificationId);
        }
    }
}
