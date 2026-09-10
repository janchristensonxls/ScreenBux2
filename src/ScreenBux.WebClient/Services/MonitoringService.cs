using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebClient.Services;

public class MonitoringService : IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    private readonly ILogger<MonitoringService> _logger;

    public event EventHandler<ProcessInfo>? ProcessDetected;
    public event EventHandler<PolicyConfiguration>? PolicyUpdated;
    public event EventHandler<GrantDto>? GrantUpdated;
    public event EventHandler<string>? StatusReceived;
    public event EventHandler<ProcessListResult>? ProcessListReceived;
    public event EventHandler<(Guid RequestId, int ImageCount)>? ScreenCaptureReady;
    public event EventHandler<NotificationAcknowledgedDto>? NotificationAcknowledged;

    public MonitoringService(ILogger<MonitoringService> logger, IConfiguration configuration, TokenProvider tokenProvider)
    {
        _logger = logger;

        var hubUrl = configuration["MonitoringHubUrl"];
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            hubUrl = "https://localhost:44323/monitoringHub";
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(tokenProvider.Token);
            })
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        _hubConnection.On<ProcessInfo>("ProcessDetected", (processInfo) =>
        {
            _logger.LogInformation("Process detected: {ProcessName}", processInfo.ProcessName);
            ProcessDetected?.Invoke(this, processInfo);
        });

        _hubConnection.On<PolicyConfiguration>("PolicyUpdated", (config) =>
        {
            _logger.LogInformation("Policy updated");
            PolicyUpdated?.Invoke(this, config);
        });

        _hubConnection.On<GrantDto>("GrantUpdated", (grant) =>
        {
            _logger.LogInformation("Grant updated for device {DeviceId}", grant.DeviceId);
            GrantUpdated?.Invoke(this, grant);
        });

        _hubConnection.On<object>("ReceiveStatus", (status) =>
        {
            _logger.LogInformation("Status received");
            StatusReceived?.Invoke(this, status.ToString() ?? "Unknown");
        });

        _hubConnection.On<ProcessListResult>("ProcessListReceived", (result) =>
        {
            _logger.LogInformation("Process list received for device {DeviceId} (request {RequestId})", result.DeviceId, result.RequestId);
            ProcessListReceived?.Invoke(this, result);
        });

        _hubConnection.On<Guid, int>("ScreenCaptureReady", (requestId, imageCount) =>
        {
            _logger.LogInformation("Screen capture ready (request {RequestId}, {ImageCount} images)", requestId, imageCount);
            ScreenCaptureReady?.Invoke(this, (requestId, imageCount));
        });

        _hubConnection.On<NotificationAcknowledgedDto>("NotificationAcknowledged", (ack) =>
        {
            _logger.LogInformation("Notification {NotificationId} acknowledged by device {DeviceId} (shown={Shown})", ack.NotificationId, ack.DeviceId, ack.Shown);
            NotificationAcknowledged?.Invoke(this, ack);
        });
    }

    public async Task StartAsync()
    {
        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to monitoring hub");
        }
    }

    public async Task StopAsync()
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.StopAsync();
            _logger.LogInformation("Disconnected from monitoring hub");
        }
    }

    public async Task RequestStatusAsync()
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("GetStatus");
        }
    }

    public async Task CloseProcessAsync(int processId)
    {
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("CloseProcess", processId);
        }
    }

    /// <summary>
    /// Requests an on-demand process list from the given device. The result arrives
    /// asynchronously via <see cref="ProcessListReceived"/>, correlated by the returned
    /// request id. Callers should apply their own timeout since the device may be offline.
    /// </summary>
    public async Task<Guid> RequestProcessListAsync(Guid deviceId)
    {
        var requestId = Guid.NewGuid();
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("RequestProcessList", deviceId, requestId);
        }

        return requestId;
    }

    /// <summary>
    /// Requests an on-demand screen capture from the given device. The result arrives
    /// asynchronously via <see cref="ScreenCaptureReady"/>, correlated by the returned request
    /// id; the images themselves are downloaded separately via REST, not through SignalR.
    /// </summary>
    public async Task<Guid> RequestScreenCaptureAsync(Guid deviceId)
    {
        var requestId = Guid.NewGuid();
        if (_hubConnection.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeAsync("RequestScreenCapture", deviceId, requestId);
        }

        return requestId;
    }

    public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
