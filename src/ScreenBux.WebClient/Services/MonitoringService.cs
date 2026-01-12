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
    public event EventHandler<string>? StatusReceived;

    public MonitoringService(ILogger<MonitoringService> logger, IConfiguration configuration)
    {
        _logger = logger;

        var hubUrl = configuration["MonitoringHubUrl"];
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            hubUrl = "https://localhost:44323/monitoringHub";
        }
        
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
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

        _hubConnection.On<object>("ReceiveStatus", (status) =>
        {
            _logger.LogInformation("Status received");
            StatusReceived?.Invoke(this, status.ToString() ?? "Unknown");
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

    public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
