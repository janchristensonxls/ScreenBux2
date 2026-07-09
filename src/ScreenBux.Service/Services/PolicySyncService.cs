using Microsoft.AspNetCore.SignalR.Client;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Maintains the SignalR connection to the web server: receives policy updates
/// and relays process detections to the monitoring UI.
/// </summary>
public class PolicySyncService : BackgroundService
{
    private readonly ILogger<PolicySyncService> _logger;
    private readonly IConfiguration _configuration;
    private readonly PolicyService _policyService;
    private HubConnection? _hubConnection;

    public PolicySyncService(
        ILogger<PolicySyncService> logger,
        IConfiguration configuration,
        PolicyService policyService)
    {
        _logger = logger;
        _configuration = configuration;
        _policyService = policyService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _configuration["MonitoringHubUrl"] ?? "https://localhost:44323/monitoringHub";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<PolicyConfiguration>("PolicyUpdated", async policy =>
        {
            _logger.LogInformation("Policy update received from SignalR");
            await _policyService.UpdatePolicyAsync(policy);
        });

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "SignalR connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _logger.LogInformation("SignalR reconnected");
            return Task.CompletedTask;
        };

        _hubConnection.Closed += async error =>
        {
            _logger.LogWarning(error, "SignalR connection closed");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _hubConnection.StartAsync(stoppingToken);
                _logger.LogInformation("SignalR connected to {HubUrl}", hubUrl);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to SignalR, retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // ignore
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync(cancellationToken);
            await _hubConnection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Relays a process detection to the web server hub so the monitoring UI updates live.
    /// No-op when the hub connection is not established.
    /// </summary>
    public async Task SendProcessDetectionAsync(ProcessInfo processInfo)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            await connection.InvokeAsync("BroadcastProcessDetection", processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send process detection to hub");
        }
    }
}
