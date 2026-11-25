using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Services;
using ScreenBux.Shared.Utilities;
using ScreenBux.WebServer.Hubs;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class PolicyController : ControllerBase
{
    private readonly ILogger<PolicyController> _logger;
    private readonly IPolicyStore _policyStore;
    private readonly IHubContext<MonitoringHub> _hubContext;

    public PolicyController(
        ILogger<PolicyController> logger,
        IPolicyStore policyStore,
        IHubContext<MonitoringHub> hubContext)
    {
        _logger = logger;
        _policyStore = policyStore;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<ActionResult<PolicyConfiguration>> GetPolicy(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        try
        {
            var policy = await _policyStore.GetPolicyAsync(accountId, cancellationToken);
            return Ok(policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving policy");
            return StatusCode(500, new { message = "Error retrieving policy" });
        }
    }

    [HttpPut]
    public async Task<ActionResult> UpdatePolicy([FromBody] PolicyConfiguration policy, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        try
        {
            var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
            await _policyStore.SavePolicyAsync(accountId, policy, cancellationToken);

            // Notify the devices whose bundle includes the default mode (the one this legacy endpoint edits).
            await PushChangedDevicesAsync(accountId, before, cancellationToken);

            _logger.LogInformation("Policy updated successfully for account {AccountId}", accountId);
            return Ok(new { message = "Policy updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating policy");
            return StatusCode(500, new { message = "Error updating policy" });
        }
    }

    [HttpPost("reload")]
    public ActionResult ReloadPolicy()
    {
        _logger.LogInformation("Policy reload requested");
        return Ok(new { message = "Policy reload requested" });
    }

    [HttpGet("profiles")]
    public async Task<ActionResult<IReadOnlyList<PolicyProfileDto>>> GetProfiles(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var profiles = await _policyStore.GetProfilesAsync(accountId, cancellationToken);
        return Ok(profiles);
    }

    [HttpPost("profiles")]
    public async Task<ActionResult<PolicyProfileDto>> CreateProfile([FromBody] PolicyProfileDto request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var profile = await _policyStore.CreateProfileAsync(accountId, request.Name, request.Policy, cancellationToken);
        return Ok(profile);
    }

    [HttpPut("profiles/{profileId}")]
    public async Task<ActionResult<PolicyProfileDto>> UpdateProfile(Guid profileId, [FromBody] PolicyProfileDto request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
        var profile = await _policyStore.UpdateProfileAsync(accountId, profileId, request.Name, request.Policy, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        // Any device whose bundle includes this mode (as default, scheduled, or override).
        await PushChangedDevicesAsync(accountId, before, cancellationToken);

        return Ok(profile);
    }

    [HttpDelete("profiles/{profileId}")]
    public async Task<ActionResult> DeleteProfile(Guid profileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var deleted = await _policyStore.DeleteProfileAsync(accountId, profileId, cancellationToken);
        if (!deleted)
        {
            return BadRequest(new { message = "Mode not found or is in use (as a default, in a schedule, or by a temporary switch)" });
        }

        return Ok(new { message = "Profile deleted" });
    }

    [HttpGet("assignments")]
    public async Task<ActionResult<PolicyAssignmentsDto>> GetAssignments(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        return Ok(await _policyStore.GetAssignmentsAsync(accountId, cancellationToken));
    }

    /// <summary>
    /// Activates a mode. With <paramref name="childProfileId"/> it applies to that child's devices
    /// only; without it, it becomes the account default (used by unassigned devices and by
    /// children that follow the default).
    /// </summary>
    [HttpPost("profiles/{profileId}/activate")]
    public async Task<ActionResult<PolicyConfiguration>> ActivateProfile(Guid profileId, [FromQuery] Guid? childProfileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
        var policy = await _policyStore.SetActiveProfileAsync(accountId, profileId, childProfileId, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Profile {ProfileId} activated for account {AccountId} (child {ChildProfileId})", profileId, accountId, childProfileId);

        return Ok(policy);
    }

    /// <summary>Removes a child's own mode so it follows the account default again.</summary>
    [HttpDelete("children/{childProfileId}/assignment")]
    public async Task<IActionResult> ClearChildAssignment(Guid childProfileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
        if (!await _policyStore.ClearChildAssignmentAsync(accountId, childProfileId, cancellationToken))
        {
            return NotFound();
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Child {ChildProfileId} on account {AccountId} now follows the default mode", childProfileId, accountId);

        return NoContent();
    }

    /// <summary>Makes a mode the default and puts every child and device on it ("Set everyone to...").</summary>
    [HttpPost("profiles/{profileId}/activate-all")]
    public async Task<ActionResult<PolicyConfiguration>> ActivateProfileForEveryone(Guid profileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
        var policy = await _policyStore.SetActiveProfileForEveryoneAsync(accountId, profileId, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Profile {ProfileId} activated for everyone on account {AccountId}", profileId, accountId);

        return Ok(policy);
    }

    // --- Mode schedule / temporary override (docs/decisions/mode-scheduling-and-temporary-overrides.md) ---

    /// <summary>Configured and current mode for the account default and every child.</summary>
    [HttpGet("modes")]
    public async Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> GetModes(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        return Ok(await _policyStore.GetModeStatusAsync(accountId, DateTime.UtcNow, cancellationToken));
    }

    /// <summary>
    /// Replaces the schedule of the account default, or of one child (<paramref name="childProfileId"/>).
    /// An empty list for a child means "no schedule" (not following the account's).
    /// </summary>
    [HttpPut("schedule")]
    public async Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> SetSchedule([FromQuery] Guid? childProfileId, [FromBody] List<ModeScheduleEntry> schedule, CancellationToken cancellationToken)
    {
        var invalid = schedule.FirstOrDefault(e => !IsValidWindow(e.Window));
        if (invalid is not null)
        {
            return BadRequest(new { message = "Schedule times must be between 00:00 and 23:59, and start and end must differ." });
        }

        return await ChangeModesAsync(
            accountId => _policyStore.SetScheduleAsync(accountId, childProfileId, schedule, cancellationToken),
            "schedule set", childProfileId, cancellationToken);
    }

    /// <summary>Removes a scope's own schedule: a child follows the account schedule again; the account has none.</summary>
    [HttpDelete("schedule")]
    public Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> ClearSchedule([FromQuery] Guid? childProfileId, CancellationToken cancellationToken) =>
        ChangeModesAsync(
            accountId => _policyStore.SetScheduleAsync(accountId, childProfileId, null, cancellationToken),
            "schedule cleared", childProfileId, cancellationToken);

    /// <summary>Temporarily switches the account default, or one child, to a mode.</summary>
    [HttpPost("override")]
    public async Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> SetOverride([FromQuery] Guid? childProfileId, [FromBody] SetModeOverrideRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetExpiry(request, out var expiresAtUtc, out var error))
        {
            return BadRequest(new { message = error });
        }

        return await ChangeModesAsync(
            accountId => _policyStore.SetOverrideAsync(accountId, childProfileId, request.ProfileId, expiresAtUtc, request.EndsAtNextScheduleChange, cancellationToken),
            "override set", childProfileId, cancellationToken);
    }

    /// <summary>Temporarily switches everyone (account default and every child) to a mode.</summary>
    [HttpPost("override/everyone")]
    public async Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> SetOverrideForEveryone([FromBody] SetModeOverrideRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetExpiry(request, out var expiresAtUtc, out var error))
        {
            return BadRequest(new { message = error });
        }

        return await ChangeModesAsync(
            accountId => _policyStore.SetOverrideForEveryoneAsync(accountId, request.ProfileId, expiresAtUtc, request.EndsAtNextScheduleChange, cancellationToken),
            "override set for everyone", null, cancellationToken);
    }

    /// <summary>Ends a scope's temporary override now.</summary>
    [HttpDelete("override")]
    public Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> ClearOverride([FromQuery] Guid? childProfileId, CancellationToken cancellationToken) =>
        ChangeModesAsync(
            accountId => _policyStore.ClearOverrideAsync(accountId, childProfileId, cancellationToken),
            "override cleared", childProfileId, cancellationToken);

    private async Task<ActionResult<IReadOnlyList<ModeScopeStatusDto>>> ChangeModesAsync(
        Func<string, Task<ModeUpdateResult>> change, string what, Guid? childProfileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);
        switch (await change(accountId))
        {
            case ModeUpdateResult.NotFound:
                return NotFound();
            case ModeUpdateResult.UnknownProfile:
                return BadRequest(new { message = "Unknown mode." });
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Mode {What} for account {AccountId} (child {ChildProfileId})", what, accountId, childProfileId);

        return Ok(await _policyStore.GetModeStatusAsync(accountId, DateTime.UtcNow, cancellationToken));
    }

    private static bool IsValidWindow(TimeWindow window) =>
        window.StartTime >= TimeSpan.Zero && window.StartTime < TimeSpan.FromDays(1)
        && window.EndTime >= TimeSpan.Zero && window.EndTime < TimeSpan.FromDays(1)
        && window.StartTime != window.EndTime;

    private static bool TryGetExpiry(SetModeOverrideRequest request, out DateTime? expiresAtUtc, out string? error)
    {
        expiresAtUtc = null;
        error = null;

        if (request.DurationMinutes is not null && request.ExpiresAtUtc is not null)
        {
            error = "Set either a duration or an end time, not both.";
            return false;
        }

        if (request.DurationMinutes is int minutes)
        {
            if (minutes <= 0)
            {
                error = "Duration must be positive.";
                return false;
            }

            expiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);
        }
        else if (request.ExpiresAtUtc is DateTime end)
        {
            expiresAtUtc = end.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : end.ToUniversalTime();
            if (expiresAtUtc <= DateTime.UtcNow)
            {
                error = "End time must be in the future.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Pushes to every device whose <see cref="DevicePolicyBundle"/> changed since
    /// <paramref name="before"/> - a mode assignment, schedule, override, or the content of any
    /// mode the bundle includes. Sent over each device's own SignalR group, never the account
    /// group: devices join it too, and different devices on one account run different policies.
    /// </summary>
    private async Task PushChangedDevicesAsync(string accountId, IReadOnlyDictionary<Guid, DeviceBundle> before, CancellationToken cancellationToken)
    {
        // Stamp before reading, so a bundle built from older data (e.g. a concurrent poll that
        // read before this change was saved) always carries an older stamp than this push.
        var now = DateTime.UtcNow;
        var after = await _policyStore.GetDeviceBundlesAsync(accountId, cancellationToken);

        foreach (var (deviceId, device) in after)
        {
            if (before.TryGetValue(deviceId, out var old) && SameBundle(old.Bundle, device.Bundle))
            {
                continue;
            }

            var group = _hubContext.Clients.Group(MonitoringHub.GetDeviceGroupName(deviceId));
            device.Bundle.GeneratedAtUtc = now;
            await group.SendAsync("PolicyBundleUpdated", device.Bundle, cancellationToken);

            // Clients that predate bundles: the policy in effect right now. They pick up later
            // schedule/override transitions through their periodic GET .../policy.
            var (policy, _) = ModeResolver.ResolvePolicy(device.Bundle, now, ModeResolver.FindTimeZoneOrUtc(device.TimeZoneId));
            await group.SendAsync("PolicyUpdated", policy, cancellationToken);
        }
    }

    private static bool SameBundle(DevicePolicyBundle a, DevicePolicyBundle b) =>
        JsonSerializer.Serialize(a, PolicyJsonOptions.Default) == JsonSerializer.Serialize(b, PolicyJsonOptions.Default);
}
