using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScreenBux.Shared.Models.Updates;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

/// <summary>
/// Serves the current update manifest (latest Service/Agent version + download URL) to the
/// ScreenBux.Updater component. Anonymous: the manifest itself contains no account-specific
/// data, and the updater runs unattended before any device/account context is available.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/updates")]
public class UpdatesController : ControllerBase
{
    private readonly IUpdateManifestStore _manifestStore;

    public UpdatesController(IUpdateManifestStore manifestStore)
    {
        _manifestStore = manifestStore;
    }

    [HttpGet("latest")]
    public ActionResult<UpdateManifestDto> GetLatest()
    {
        return Ok(_manifestStore.GetLatestManifest());
    }
}
