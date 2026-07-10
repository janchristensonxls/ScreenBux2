using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScreenBux.Shared.Models;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Hubs;

/// <summary>
/// SignalR hub for real-time communication with web clients
/// </summary>
[Authorize]
public class MonitoringHub : Hub
{
    private readonly ILogger<MonitoringHub> _logger;

    public MonitoringHub(ILogger<MonitoringHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            // Group connections by account so parents only see their own devices,
            // and the server can target a specific account's devices.
            await Groups.AddToGroupAsync(Context.ConnectionId, accountId);
        }

        _logger.LogInformation("Client connected: {ConnectionId} (account {AccountId})",
            Context.ConnectionId, accountId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, accountId);
        }

        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client requests to get current status
    /// </summary>
    public async Task GetStatus()
    {
        _logger.LogInformation("Client {ConnectionId} requested status", Context.ConnectionId);
        await Clients.Caller.SendAsync("ReceiveStatus", new
        {
            Timestamp = DateTime.UtcNow,
            Message = "Service is running"
        });
    }

    /// <summary>
    /// Client requests to close a process
    /// </summary>
    public async Task CloseProcess(int processId)
    {
        _logger.LogInformation("Client {ConnectionId} requested to close process {ProcessId}", 
            Context.ConnectionId, processId);

        // This would communicate with the Windows Service
        // For now, just acknowledge the request
        await Clients.Caller.SendAsync("ProcessClosedResponse", new
        {
            ProcessId = processId,
            Success = true,
            Message = "Close request sent"
        });
    }

    /// <summary>
    /// Server broadcasts process detection to the owning account's clients
    /// </summary>
    public async Task BroadcastProcessDetection(ProcessInfo processInfo)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Clients.Group(accountId).SendAsync("ProcessDetected", processInfo);
        }
    }

    /// <summary>
    /// Server broadcasts policy update to the owning account's clients
    /// </summary>
    public async Task BroadcastPolicyUpdate(PolicyConfiguration config)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Clients.Group(accountId).SendAsync("PolicyUpdated", config);
        }
    }
}
