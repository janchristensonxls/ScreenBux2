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
    private const int MaxHistoryDays = 366;

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

    /// <summary>
    /// Parent (or a device token for one of the child's devices) fetches a usage history, ending
    /// today (or an optional end date), for stats/trend views. Either <paramref name="days"/> or an
    /// explicit <paramref name="startDate"/> (which takes precedence) sets the range length.
    /// </summary>
    [HttpGet("{childProfileId:guid}/history")]
    public async Task<ActionResult<List<UsageSummaryDto>>> GetHistory(Guid childProfileId, [FromQuery] int days = 7, [FromQuery] DateOnly? endDate = null, [FromQuery] DateOnly? startDate = null, CancellationToken cancellationToken = default)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (startDate is DateOnly start)
        {
            if (start > end)
            {
                return BadRequest(new { message = "StartDate must not be after EndDate." });
            }

            days = end.DayNumber - start.DayNumber + 1;
        }

        if (days is < 1 or > MaxHistoryDays)
        {
            return BadRequest(new { message = $"The range must be between 1 and {MaxHistoryDays} days." });
        }

        try
        {
            var history = await _usageStore.GetHistoryAsync(accountId, childProfileId, end, days, cancellationToken);
            return Ok(history);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
