using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly GrantService _grantService;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly IServiceProvider _serviceProvider;
    private HubConnection? _hubConnection;

    public PolicySyncService(
        ILogger<PolicySyncService> logger,
        IConfiguration configuration,
        PolicyService policyService,
        GrantService grantService,
        DeviceIdentityService deviceIdentity,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _policyService = policyService;
        _grantService = grantService;
        _deviceIdentity = deviceIdentity;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _configuration["MonitoringHubUrl"] ?? "https://localhost:44323/monitoringHub";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () =>
                {
                    var state = _deviceIdentity.GetOrCreate();
                    return Task.FromResult(state.DeviceToken);
                };
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<PolicyConfiguration>("PolicyUpdated", async policy =>
        {
            _logger.LogInformation("Policy update received from SignalR");
            await _policyService.UpdatePolicyAsync(policy);
            _policyService.MarkSyncedSinceStartup();
        });

        _hubConnection.On<GrantDto>("GrantUpdated", async grant =>
        {
            _logger.LogInformation("Grant update received from SignalR; expires at {ExpiresAtUtc}", grant.ExpiresAtUtc);
            await _grantService.UpdateGrantAsync(grant.ExpiresAtUtc);
        });

        _hubConnection.On<Guid>("ProcessListRequested", async requestId =>
        {
            _logger.LogInformation("Process list requested via SignalR (request {RequestId}).", requestId);
            await HandleProcessListRequestedAsync(requestId);
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
            // Wait until this device is linked and has a token before connecting.
            var state = _deviceIdentity.GetOrCreate();
            if (string.IsNullOrEmpty(state.DeviceToken))
            {
                _logger.LogDebug("Device not yet linked; waiting before connecting to SignalR hub.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

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

    /// <summary>
    /// Handles an on-demand "get process list" request pushed from the web server's hub.
    /// Resolves <see cref="ProcessMonitoringService"/> lazily via <see cref="_serviceProvider"/>
    /// rather than a constructor dependency, since ProcessMonitoringService itself depends on
    /// this class (to report detections) and a direct two-way constructor dependency would be
    /// a circular reference.
    /// </summary>
    private async Task HandleProcessListRequestedAsync(Guid requestId)
    {
        var connection = _hubConnection;
        if (connection is not { State: HubConnectionState.Connected })
        {
            return;
        }

        var deviceId = _deviceIdentity.GetOrCreate().DeviceId;
        var result = new ProcessListResult
        {
            RequestId = requestId,
            DeviceId = deviceId
        };

        try
        {
            var processMonitoring = _serviceProvider.GetRequiredService<ProcessMonitoringService>();
            result.Processes.AddRange(processMonitoring.GetCurrentProcesses());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate processes for on-demand process list request {RequestId}.", requestId);
            result.Success = false;
            result.ErrorMessage = "Failed to enumerate processes on this device.";
        }

        try
        {
            await connection.InvokeAsync("ReportProcessList", result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send process list result to hub for request {RequestId}.", requestId);
        }
    }
}
