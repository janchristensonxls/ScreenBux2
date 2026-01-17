using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.WebServer.Contracts;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Models;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDeviceSecretProtector _secretProtector;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        AppDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IDeviceSecretProtector secretProtector,
        ILogger<DevicesController> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<IEnumerable<DeviceSummary>>> GetDevices([FromQuery] Guid householdId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var hasAccess = await _dbContext.UserHouseholds
            .AnyAsync(uh => uh.UserId == user.Id && uh.HouseholdId == householdId);
        if (!hasAccess)
        {
            return Forbid();
        }

        var devices = await _dbContext.Devices
            .Where(d => d.HouseholdId == householdId && !d.IsRevoked)
            .Select(d => new DeviceSummary(
                d.DeviceId,
                d.DisplayName,
                d.MachineName,
                d.OsVersion,
                d.AgentVersion,
                d.ServiceVersion,
                d.OnlineAgent,
                d.OnlineService,
                d.LastSeenAt))
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost("pair")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> PairDevice([FromBody] PairDeviceRequest request)
    {
        var pairingCode = await _dbContext.PairingCodes
            .Include(pc => pc.Household)
            .FirstOrDefaultAsync(pc => pc.Code == request.PairingCode);

        if (pairingCode == null || pairingCode.UsedAt.HasValue || pairingCode.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Pairing code is invalid or expired." });
        }

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId);

        if (device == null)
        {
            device = new Device
            {
                DeviceId = request.DeviceId,
                HouseholdId = pairingCode.HouseholdId,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Devices.Add(device);
        }
        else if (device.HouseholdId != pairingCode.HouseholdId)
        {
            return BadRequest(new { message = "Device already belongs to another household." });
        }

        device.DisplayName = request.DeviceName;
        device.MachineName = request.MachineName;
        device.OsVersion = request.OsVersion;
        device.AgentVersion = request.AgentVersion;
        device.ServiceVersion = request.ServiceVersion;
        device.IsRevoked = false;
        device.EncryptedSecret = _secretProtector.Protect(request.DeviceSecret);
        device.LastSeenAt = DateTime.UtcNow;

        pairingCode.UsedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Paired device {DeviceId} to household {HouseholdId} at {Timestamp}",
            device.DeviceId,
            device.HouseholdId,
            DateTime.UtcNow);

        return Ok(new { deviceId = device.DeviceId, householdId = device.HouseholdId });
    }
}
