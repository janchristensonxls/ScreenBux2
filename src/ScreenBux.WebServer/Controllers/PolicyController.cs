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

            // Notify this account's connected devices/clients.
            await _hubContext.Clients.Group(accountId).SendAsync("PolicyUpdated", policy, cancellationToken);

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
            await _hubContext.Clients.Group(accountId).SendAsync("PolicyUpdated", profile.Policy, cancellationToken);
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
            return BadRequest(new { message = "Profile not found or is currently active" });
        }

        return Ok(new { message = "Profile deleted" });
    }

    [HttpPost("profiles/{profileId}/activate")]
    public async Task<ActionResult<PolicyConfiguration>> ActivateProfile(Guid profileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var policy = await _policyStore.SetActiveProfileAsync(accountId, profileId, cancellationToken);
        if (policy is null)
        {
            return NotFound();
        }

        await _hubContext.Clients.Group(accountId).SendAsync("PolicyUpdated", policy, cancellationToken);
        _logger.LogInformation("Profile {ProfileId} activated for account {AccountId}", profileId, accountId);

        return Ok(policy);
    }
}
