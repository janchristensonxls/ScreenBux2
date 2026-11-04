using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ScreenBux.Shared.Models;
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
            await _policyStore.SavePolicyAsync(accountId, policy, cancellationToken);

            // Notify the devices that use the default mode (the one this legacy endpoint edits).
            var assignments = await _policyStore.GetAssignmentsAsync(accountId, cancellationToken);
            await PushToDevicesUsingAsync(accountId, assignments.DefaultProfileId, cancellationToken);

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

        var profile = await _policyStore.UpdateProfileAsync(accountId, profileId, request.Name, request.Policy, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.IsActive)
        {
            await PushToDevicesUsingAsync(accountId, profileId, cancellationToken);
        }

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
            return BadRequest(new { message = "Mode not found or is in use (by the default or a child)" });
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

        var before = await _policyStore.GetDeviceProfileMapAsync(accountId, cancellationToken);
        var policy = await _policyStore.SetActiveProfileAsync(accountId, profileId, childProfileId, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Profile {ProfileId} activated for account {AccountId} (child {ChildProfileId})", profileId, accountId, childProfileId);

        return Ok(policy);
    }

    /// <summary>Makes a mode the default and puts every child on it ("Set everyone to...").</summary>
    [HttpPost("profiles/{profileId}/activate-all")]
    public async Task<ActionResult<PolicyConfiguration>> ActivateProfileForEveryone(Guid profileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var before = await _policyStore.GetDeviceProfileMapAsync(accountId, cancellationToken);
        var policy = await _policyStore.SetActiveProfileForEveryoneAsync(accountId, profileId, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        await PushChangedDevicesAsync(accountId, before, cancellationToken);
        _logger.LogInformation("Profile {ProfileId} activated for everyone on account {AccountId}", profileId, accountId);

        return Ok(policy);
    }

    /// <summary>Pushes the effective policy to every device whose mode changed since <paramref name="before"/>.</summary>
    private async Task PushChangedDevicesAsync(string accountId, IReadOnlyDictionary<Guid, Guid> before, CancellationToken cancellationToken)
    {
        var after = await _policyStore.GetDeviceProfileMapAsync(accountId, cancellationToken);
        var changed = after.Where(kv => !before.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key);
        await PushToDevicesAsync(accountId, changed, cancellationToken);
    }

    /// <summary>Pushes the (edited) policy to every device whose effective mode is <paramref name="profileId"/>.</summary>
    private async Task PushToDevicesUsingAsync(string accountId, Guid profileId, CancellationToken cancellationToken)
    {
        var map = await _policyStore.GetDeviceProfileMapAsync(accountId, cancellationToken);
        await PushToDevicesAsync(accountId, map.Where(kv => kv.Value == profileId).Select(kv => kv.Key), cancellationToken);
    }

    /// <summary>
    /// Sends each device its own effective policy over its per-device SignalR group. Policy
    /// updates must not go to the account group: devices join it too, and with per-child modes
    /// different devices on one account can run different policies.
    /// </summary>
    private async Task PushToDevicesAsync(string accountId, IEnumerable<Guid> deviceIds, CancellationToken cancellationToken)
    {
        foreach (var deviceId in deviceIds)
        {
            var policy = await _policyStore.GetDevicePolicyAsync(deviceId, accountId, cancellationToken);
            await _hubContext.Clients.Group(MonitoringHub.GetDeviceGroupName(deviceId)).SendAsync("PolicyUpdated", policy, cancellationToken);
        }
    }
}
