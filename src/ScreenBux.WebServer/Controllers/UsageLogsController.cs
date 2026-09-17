using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Shared.Models;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

/// <summary>Request body the Service POSTs to upload a completed on-demand usage-logs fetch.</summary>
public class UsageLogsUploadRequest
{
    public Guid DeviceId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<UsageDayLog> Days { get; set; } = new();
}

[ApiController]
[Route("api/[controller]")]
public class UsageLogsController : ControllerBase
{
    private readonly ILogger<UsageLogsController> _logger;
    private readonly AppDbContext _db;
    private readonly IUsageLogsStore _logsStore;

    public UsageLogsController(
        ILogger<UsageLogsController> logger,
        AppDbContext db,
        IUsageLogsStore logsStore)
    {
        _logger = logger;
        _db = db;
        _logsStore = logsStore;
    }

    /// <summary>Service uploads a completed usage-logs fetch, authenticated with its device token.</summary>
    [HttpPost("{requestId:guid}")]
    [Authorize]
    public async Task<IActionResult> Upload(Guid requestId, [FromBody] UsageLogsUploadRequest request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        var callerDeviceId = User.GetDeviceId();

        if (accountId is null || callerDeviceId is null)
        {
            return Unauthorized();
        }

        // A device token may only upload usage logs for itself.
        if (callerDeviceId != request.DeviceId)
        {
            return Forbid();
        }

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.AccountId == accountId, cancellationToken);

        if (device is null)
        {
            return NotFound();
        }

        if (!request.Success)
        {
            _logger.LogWarning("Usage logs fetch {RequestId} for device {DeviceId} failed: {Error}", requestId, request.DeviceId, request.ErrorMessage);
            return BadRequest(new { message = request.ErrorMessage ?? "Fetching usage logs failed." });
        }

        _logsStore.Add(requestId, new CachedUsageLogs(accountId, request.DeviceId, request.Days, DateTime.UtcNow.Add(InMemoryUsageLogsStore.DefaultTtl)));

        _logger.LogInformation("Stored usage logs {RequestId} for device {DeviceId} ({DayCount} days)", requestId, request.DeviceId, request.Days.Count);
        return Ok();
    }

    /// <summary>Parent downloads a completed usage-logs fetch.</summary>
    [HttpGet("{requestId:guid}")]
    [Authorize]
    public IActionResult Get(Guid requestId)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var logs = _logsStore.Get(requestId);
        if (logs is null || logs.AccountId != accountId)
        {
            return NotFound();
        }

        return Ok(logs.Days);
    }
}
