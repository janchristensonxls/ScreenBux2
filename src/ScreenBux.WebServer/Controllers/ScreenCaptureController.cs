using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Shared.Models;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

/// <summary>Request body the Service POSTs to upload a completed on-demand screen capture.</summary>
public class ScreenCaptureUploadRequest
{
    public Guid DeviceId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<CapturedImage> Images { get; set; } = new();
}

[ApiController]
[Route("api/[controller]")]
public class ScreenCaptureController : ControllerBase
{
    private readonly ILogger<ScreenCaptureController> _logger;
    private readonly AppDbContext _db;
    private readonly IScreenCaptureStore _captureStore;

    public ScreenCaptureController(
        ILogger<ScreenCaptureController> logger,
        AppDbContext db,
        IScreenCaptureStore captureStore)
    {
        _logger = logger;
        _db = db;
        _captureStore = captureStore;
    }

    /// <summary>Service uploads a completed screen capture, authenticated with its device token.</summary>
    [HttpPost("{requestId:guid}")]
    [Authorize]
    public async Task<IActionResult> Upload(Guid requestId, [FromBody] ScreenCaptureUploadRequest request, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        var callerDeviceId = User.GetDeviceId();

        if (accountId is null || callerDeviceId is null)
        {
            return Unauthorized();
        }

        // A device token may only upload captures for itself.
        if (callerDeviceId != request.DeviceId)
        {
            return Forbid();
        }

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId && d.AccountId == accountId, cancellationToken);

        if (device is null)
        {
            return NotFound();
        }

        if (!request.Success)
        {
            _logger.LogWarning("Screen capture {RequestId} for device {DeviceId} failed: {Error}", requestId, request.DeviceId, request.ErrorMessage);
            return BadRequest(new { message = request.ErrorMessage ?? "Screen capture failed." });
        }

        var images = request.Images
            .Select(i => new CachedCaptureImage(i.Index, i.WidthPx, i.HeightPx, i.ImageBytes))
            .OrderBy(i => i.Index)
            .ToList();

        _captureStore.Add(requestId, new CachedCapture(accountId, request.DeviceId, images, DateTime.UtcNow.Add(InMemoryScreenCaptureStore.DefaultTtl)));

        _logger.LogInformation("Stored screen capture {RequestId} for device {DeviceId} ({ImageCount} images)", requestId, request.DeviceId, images.Count);
        return Ok();
    }

    /// <summary>Parent downloads a single monitor's image from a completed capture.</summary>
    [HttpGet("{requestId:guid}/{index:int}")]
    [Authorize]
    public IActionResult Download(Guid requestId, int index)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var capture = _captureStore.Get(requestId);
        if (capture is null || capture.AccountId != accountId)
        {
            return NotFound();
        }

        var image = capture.Images.FirstOrDefault(i => i.Index == index);
        if (image is null)
        {
            return NotFound();
        }

        return File(image.ImageBytes, "image/jpeg");
    }

    /// <summary>Parent fetches metadata (image count/dimensions) for a completed capture.</summary>
    [HttpGet("{requestId:guid}")]
    [Authorize]
    public IActionResult GetMetadata(Guid requestId)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var capture = _captureStore.Get(requestId);
        if (capture is null || capture.AccountId != accountId)
        {
            return NotFound();
        }

        return Ok(capture.Images.Select(i => new { i.Index, i.WidthPx, i.HeightPx }));
    }
}
