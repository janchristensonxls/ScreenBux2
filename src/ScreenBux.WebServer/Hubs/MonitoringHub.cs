using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
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
    private readonly AppDbContext _db;

    public MonitoringHub(ILogger<MonitoringHub> logger, AppDbContext db)
    {
        _logger = logger;
        _db = db;
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

        // Device-token connections (the Service acting as a SignalR client) additionally join
        // a per-device group so the server can target exactly one device - e.g. to request an
        // on-demand process list or screenshot - without fanning the request out to every
        // device on the account.
        var deviceId = Context.User?.GetDeviceId();
        if (deviceId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetDeviceGroupName(deviceId.Value));
        }

        _logger.LogInformation("Client connected: {ConnectionId} (account {AccountId}, device {DeviceId})",
            Context.ConnectionId, accountId, deviceId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, accountId);
        }

        var deviceId = Context.User?.GetDeviceId();
        if (deviceId.HasValue)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetDeviceGroupName(deviceId.Value));
        }

        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static string GetDeviceGroupName(Guid deviceId) => $"device:{deviceId}";

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

    /// <summary>
    /// Called by a WebClient to request an on-demand process list from a specific device.
    /// Verifies the caller's account actually owns the device before addressing it, then
    /// forwards the request to that device's Service connection (if any) via its own group.
    /// The result arrives asynchronously through <see cref="ReportProcessList"/>.
    /// </summary>
    public async Task RequestProcessList(Guid deviceId, Guid requestId)
    {
        var accountId = Context.User?.GetAccountId();
        if (string.IsNullOrEmpty(accountId))
        {
            return;
        }

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId && d.AccountId == accountId);
        if (!deviceExists)
        {
            _logger.LogWarning("Client {ConnectionId} (account {AccountId}) requested process list for device {DeviceId} it does not own.",
                Context.ConnectionId, accountId, deviceId);
            return;
        }

        _logger.LogInformation("Client {ConnectionId} requested process list for device {DeviceId} (request {RequestId}).",
            Context.ConnectionId, deviceId, requestId);
        await Clients.Group(GetDeviceGroupName(deviceId)).SendAsync("ProcessListRequested", requestId);
    }

    /// <summary>
    /// Called by the Service (as a SignalR client) to report the result of an on-demand
    /// process list request back to the owning account's WebClient(s).
    /// </summary>
    public async Task ReportProcessList(ProcessListResult result)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Clients.Group(accountId).SendAsync("ProcessListReceived", result);
        }
    }

    /// <summary>
    /// Called by a WebClient to request an on-demand "capture all screens" from a specific
    /// device. Mirrors <see cref="RequestProcessList"/>; the actual image bytes are never sent
    /// over SignalR - only this small control-plane signal, and later a "ready" notification
    /// once the Service has uploaded the images to the WebServer's REST endpoint.
    /// </summary>
    public async Task RequestScreenCapture(Guid deviceId, Guid requestId)
    {
        var accountId = Context.User?.GetAccountId();
        if (string.IsNullOrEmpty(accountId))
        {
            return;
        }

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId && d.AccountId == accountId);
        if (!deviceExists)
        {
            _logger.LogWarning("Client {ConnectionId} (account {AccountId}) requested screen capture for device {DeviceId} it does not own.",
                Context.ConnectionId, accountId, deviceId);
            return;
        }

        _logger.LogInformation("Client {ConnectionId} requested screen capture for device {DeviceId} (request {RequestId}).",
            Context.ConnectionId, deviceId, requestId);
        await Clients.Group(GetDeviceGroupName(deviceId)).SendAsync("ScreenCaptureRequested", requestId);
    }

    /// <summary>
    /// Called by the Service (as a SignalR client) once it has uploaded a completed screen
    /// capture to the WebServer's REST endpoint, to notify the owning account's WebClient(s)
    /// that the images are ready to download.
    /// </summary>
    public async Task ReportScreenCaptureReady(Guid requestId, int imageCount)
    {
        var accountId = Context.User?.GetAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await Clients.Group(accountId).SendAsync("ScreenCaptureReady", requestId, imageCount);
        }
    }
}
