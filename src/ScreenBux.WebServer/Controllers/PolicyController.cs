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
}
