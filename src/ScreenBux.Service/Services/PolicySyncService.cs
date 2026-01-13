using Microsoft.AspNetCore.SignalR.Client;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Syncs policy updates from the web server via SignalR.
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
        var hubUrl = _configuration["MonitoringHubUrl"] ?? "https://localhost:7225/monitoringHub";

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
}
