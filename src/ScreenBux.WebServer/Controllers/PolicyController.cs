using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;
using ScreenBux.WebServer.Hubs;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PolicyController : ControllerBase
{
    private readonly ILogger<PolicyController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<MonitoringHub> _hubContext;

    public PolicyController(
        ILogger<PolicyController> logger,
        IConfiguration configuration,
        IHubContext<MonitoringHub> hubContext)
    {
        _logger = logger;
        _configuration = configuration;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<ActionResult<PolicyConfiguration>> GetPolicy()
    {
        try
        {
            var policyPath = _configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
            
            if (!System.IO.File.Exists(policyPath))
            {
                return NotFound(new { message = "Policy file not found" });
            }

            var json = await System.IO.File.ReadAllTextAsync(policyPath);
            var policy = System.Text.Json.JsonSerializer.Deserialize<PolicyConfiguration>(json);
            
            return Ok(policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving policy");
            return StatusCode(500, new { message = "Error retrieving policy" });
        }
    }

    [HttpPut]
    public async Task<ActionResult> UpdatePolicy([FromBody] PolicyConfiguration policy)
    {
        try
        {
            var policyPath = _configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
            PolicyStorage.EnsurePolicyDirectory(policyPath);
            
            var json = System.Text.Json.JsonSerializer.Serialize(policy, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await System.IO.File.WriteAllTextAsync(policyPath, json);
            await _hubContext.Clients.All.SendAsync("PolicyUpdated", policy);
            
            _logger.LogInformation("Policy updated successfully");
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
        // This would trigger the Windows Service to reload the policy
        // For now, just acknowledge
        _logger.LogInformation("Policy reload requested");
        return Ok(new { message = "Policy reload requested" });
    }
}
