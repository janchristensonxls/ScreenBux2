using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Models;

namespace ScreenBux.WebServer.Hubs;

[Authorize(Policy = "UserOrDevice")]
public class MonitoringHub : Hub
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MonitoringHub> _logger;

    public MonitoringHub(AppDbContext dbContext, IMemoryCache cache, ILogger<MonitoringHub> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var deviceInfo = GetDeviceClaims();
        if (deviceInfo != null)
        {
            await JoinDeviceGroups(deviceInfo.Value.deviceId, deviceInfo.Value.householdId, deviceInfo.Value.clientType);
            await UpdateDeviceConnection(deviceInfo.Value.deviceId, deviceInfo.Value.clientType, true);
        }
        else
        {
            await JoinUserGroups();
        }

        _logger.LogInformation("Client connected {ConnectionId} at {Timestamp}", Context.ConnectionId, DateTime.UtcNow);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceInfo = GetDeviceClaims();
        if (deviceInfo != null)
        {
            await UpdateDeviceConnection(deviceInfo.Value.deviceId, deviceInfo.Value.clientType, false);
        }

        _logger.LogInformation("Client disconnected {ConnectionId} at {Timestamp}", Context.ConnectionId, DateTime.UtcNow);
        await base.OnDisconnectedAsync(exception);
    }

    [Authorize(Policy = "UserOnly")]
    public async Task SendCommand(Guid deviceId, string target, object command)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User not authenticated.");
        }

        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device == null)
        {
            throw new HubException("Device not found.");
        }

        var hasAccess = await _dbContext.UserHouseholds
            .AnyAsync(uh => uh.UserId == userId
                            && uh.HouseholdId == device.HouseholdId
                            && (uh.Role == HouseholdRole.Admin || uh.Role == HouseholdRole.Parent));
        if (!hasAccess)
        {
            throw new HubException("Not authorized for this device.");
        }

        var commandId = Guid.NewGuid().ToString("N");
        _cache.Set($"command:{commandId}", Context.ConnectionId, TimeSpan.FromMinutes(10));

        var payload = new
        {
            CommandId = commandId,
            Target = target,
            Command = command,
            IssuedAt = DateTime.UtcNow
        };

        switch (target.ToLowerInvariant())
        {
            case "agent":
                await Clients.Group($"device:{deviceId}:agent").SendAsync("CommandReceived", payload);
                break;
            case "service":
                await Clients.Group($"device:{deviceId}:service").SendAsync("CommandReceived", payload);
                break;
            case "both":
                await Clients.Group($"device:{deviceId}").SendAsync("CommandReceived", payload);
                break;
            default:
                throw new HubException("Invalid target.");
        }
    }

    [Authorize(Policy = "DeviceOnly")]
    public async Task ReportStatus(string clientType, object payload)
    {
        var deviceInfo = GetDeviceClaims();
        if (deviceInfo == null)
        {
            throw new HubException("Device not authenticated.");
        }

        await UpdateDeviceLastSeen(deviceInfo.Value.deviceId);

        await Clients.Group($"household:{deviceInfo.Value.householdId}")
            .SendAsync("DeviceStatus", new
            {
                DeviceId = deviceInfo.Value.deviceId,
                ClientType = clientType,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            });
    }

    [Authorize(Policy = "DeviceOnly")]
    public async Task Heartbeat(string clientType, object payload)
    {
        var deviceInfo = GetDeviceClaims();
        if (deviceInfo == null)
        {
            throw new HubException("Device not authenticated.");
        }

        await UpdateDeviceLastSeen(deviceInfo.Value.deviceId);

        await Clients.Group($"household:{deviceInfo.Value.householdId}")
            .SendAsync("DeviceHeartbeat", new
            {
                DeviceId = deviceInfo.Value.deviceId,
                ClientType = clientType,
                Payload = payload,
                Timestamp = DateTime.UtcNow
            });
    }

    [Authorize(Policy = "DeviceOnly")]
    public async Task CommandResult(string commandId, bool success, object data)
    {
        var deviceInfo = GetDeviceClaims();
        if (deviceInfo == null)
        {
            throw new HubException("Device not authenticated.");
        }

        await UpdateDeviceLastSeen(deviceInfo.Value.deviceId);

        if (_cache.TryGetValue<string>($"command:{commandId}", out var connectionId))
        {
            await Clients.Client(connectionId).SendAsync("CommandResult", new
            {
                CommandId = commandId,
                Success = success,
                Data = data,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task JoinUserGroups()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var householdIds = await _dbContext.UserHouseholds
            .Where(uh => uh.UserId == userId)
            .Select(uh => uh.HouseholdId)
            .ToListAsync();

        foreach (var householdId in householdIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"household:{householdId}");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
    }

    private async Task JoinDeviceGroups(Guid deviceId, Guid householdId, string clientType)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}:{clientType}");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"household:{householdId}");
    }

    private (Guid deviceId, Guid householdId, string clientType)? GetDeviceClaims()
    {
        if (Context.User?.FindFirstValue("role") != "device")
        {
            return null;
        }

        var deviceIdValue = Context.User.FindFirstValue("deviceId");
        var householdIdValue = Context.User.FindFirstValue("householdId");
        var clientType = Context.User.FindFirstValue("clientType") ?? string.Empty;

        if (!Guid.TryParse(deviceIdValue, out var deviceId) ||
            !Guid.TryParse(householdIdValue, out var householdId))
        {
            return null;
        }

        return (deviceId, householdId, clientType);
    }

    private async Task UpdateDeviceConnection(Guid deviceId, string clientType, bool isOnline)
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device == null)
        {
            return;
        }

        if (clientType.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            device.OnlineAgent = isOnline;
        }
        else if (clientType.Equals("service", StringComparison.OrdinalIgnoreCase))
        {
            device.OnlineService = isOnline;
        }

        device.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    private async Task UpdateDeviceLastSeen(Guid deviceId)
    {
        var device = await _dbContext.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device == null)
        {
            return;
        }

        device.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}
