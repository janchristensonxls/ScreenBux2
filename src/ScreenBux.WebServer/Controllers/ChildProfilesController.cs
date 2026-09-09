using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScreenBux.Shared.Models.Devices;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

/// <summary>REST endpoints for managing a parent account's <c>ChildProfile</c>s.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ChildProfilesController : ControllerBase
{
    private readonly ILogger<ChildProfilesController> _logger;
    private readonly IChildProfileStore _childProfileStore;

    public ChildProfilesController(ILogger<ChildProfilesController> logger, IChildProfileStore childProfileStore)
    {
        _logger = logger;
        _childProfileStore = childProfileStore;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChildProfileDto>>> GetProfiles(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var profiles = await _childProfileStore.GetProfilesAsync(accountId, cancellationToken);
        return Ok(profiles);
    }

    [HttpPost]
    public async Task<ActionResult<ChildProfileDto>> CreateProfile([FromBody] CreateChildProfileRequest request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest(new { message = "DisplayName is required." });
        }

        var profile = await _childProfileStore.CreateProfileAsync(accountId, request.DisplayName.Trim(), cancellationToken);
        return Ok(profile);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChildProfileDto>> UpdateProfile(Guid id, [FromBody] UpdateChildProfileRequest request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest(new { message = "DisplayName is required." });
        }

        var profile = await _childProfileStore.UpdateProfileAsync(accountId, id, request.DisplayName.Trim(), cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        return Ok(profile);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteProfile(Guid id, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var deleted = await _childProfileStore.DeleteProfileAsync(accountId, id, cancellationToken);
        if (!deleted)
        {
            return BadRequest(new { message = "Child profile was not found, or still has devices linked to it." });
        }

        _logger.LogInformation("Deleted child profile {ChildProfileId} for account {AccountId}", id, accountId);
        return NoContent();
    }
}
