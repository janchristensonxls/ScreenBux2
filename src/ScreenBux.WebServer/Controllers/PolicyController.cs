using Microsoft.AspNetCore.Mvc;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PolicyController : ControllerBase
{
    private readonly ILogger<PolicyController> _logger;
    private readonly IConfiguration _configuration;

    public PolicyController(ILogger<PolicyController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<PolicyConfiguration>> GetPolicy()
    {
        try
        {
            var policyPath = _configuration["PolicyFilePath"] ?? "policy.json";
            
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
            var policyPath = _configuration["PolicyFilePath"] ?? "policy.json";
            
            var json = System.Text.Json.JsonSerializer.Serialize(policy, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await System.IO.File.WriteAllTextAsync(policyPath, json);
            
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
