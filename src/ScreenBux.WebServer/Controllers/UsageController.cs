using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ScreenBux.Shared.Models;
using ScreenBux.WebServer.Hubs;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

/// <summary>
/// REST endpoints for accumulated per-day usage totals. See
/// docs/decisions/screen-time-usage-tracking.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsageController : ControllerBase
{
    private readonly ILogger<UsageController> _logger;
    private readonly IUsageStore _usageStore;
    private readonly IHubContext<MonitoringHub> _hubContext;

    public UsageController(ILogger<UsageController> logger, IUsageStore usageStore, IHubContext<MonitoringHub> hubContext)
    {
        _logger = logger;
        _usageStore = usageStore;
        _hubContext = hubContext;
    }

    /// <summary>
    /// The Service reports a delta (elapsed seconds since its last flush) for one of its
    /// devices. Only a device token for that same device (or the owning parent) may report.
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<UsageSummaryDto>> AddUsageSeconds([FromBody] AddUsageSecondsRequest request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var callerDeviceId = User.GetDeviceId();
        if (callerDeviceId is not null && callerDeviceId != request.DeviceId)
        {
            return Forbid();
        }

        if (request.Seconds < 0)
        {
            return BadRequest(new { message = "Seconds must not be negative; usage deltas are always additive." });
        }

        UsageSummaryDto summary;
        try
        {
            summary = await _usageStore.AddUsageSecondsAsync(accountId, request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        await _hubContext.Clients.Group(accountId).SendAsync("UsageUpdated", summary, cancellationToken);

        return Ok(summary);
    }

    /// <summary>Parent (or a device token for one of the child's devices) fetches a usage summary.</summary>
    [HttpGet("{childProfileId:guid}")]
    public async Task<ActionResult<UsageSummaryDto>> GetSummary(Guid childProfileId, [FromQuery] DateOnly? effectiveDate, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var date = effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var summary = await _usageStore.GetSummaryAsync(accountId, childProfileId, date, cancellationToken);
            return Ok(summary);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
