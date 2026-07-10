using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Models.Devices;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    // Unambiguous characters for human-entered codes (no 0/O, 1/I).
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    private readonly ILogger<DevicesController> _logger;
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokenService;
    private readonly IPolicyStore _policyStore;

    public DevicesController(
        ILogger<DevicesController> logger,
        AppDbContext db,
        JwtTokenService tokenService,
        IPolicyStore policyStore)
    {
        _logger = logger;
        _db = db;
        _tokenService = tokenService;
        _policyStore = policyStore;
    }

    /// <summary>Parent generates a short link code to enter on a device.</summary>
    [HttpPost("linkcode")]
    [Authorize]
    public async Task<ActionResult<LinkCodeResponse>> GenerateLinkCode([FromQuery] Guid? childProfileId, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var linkCode = new DeviceLinkCode
        {
            AccountId = accountId,
            ChildProfileId = childProfileId,
            Code = GenerateCode(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };

        _db.DeviceLinkCodes.Add(linkCode);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Generated device link code for account {AccountId}", accountId);
        return Ok(new LinkCodeResponse { Code = linkCode.Code, ExpiresAt = linkCode.ExpiresAt });
    }

    /// <summary>Device redeems a link code to bind itself and receive a device-scoped token.</summary>
    [HttpPost("redeem")]
    [AllowAnonymous]
    public async Task<ActionResult<DeviceTokenResponse>> Redeem([FromBody] RedeemLinkCodeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.MachineKey))
        {
            return BadRequest(new { message = "Code and machine key are required." });
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var linkCode = await _db.DeviceLinkCodes
            .FirstOrDefaultAsync(l => l.Code == normalizedCode, cancellationToken);

        if (linkCode is null || linkCode.RedeemedAt is not null || linkCode.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Invalid or expired link code." });
        }

        // Reuse an existing device with the same machine key, or create a new one.
        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.MachineKey == request.MachineKey, cancellationToken);

        if (device is null)
        {
            device = new Device
            {
                AccountId = linkCode.AccountId,
                ChildProfileId = linkCode.ChildProfileId,
                Name = string.IsNullOrWhiteSpace(request.DeviceName) ? "Unnamed device" : request.DeviceName,
                MachineKey = request.MachineKey,
                LinkedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            _db.Devices.Add(device);
        }
        else
        {
            device.AccountId = linkCode.AccountId;
            device.ChildProfileId = linkCode.ChildProfileId;
            if (!string.IsNullOrWhiteSpace(request.DeviceName))
            {
                device.Name = request.DeviceName;
            }
            device.LastSeenAt = DateTime.UtcNow;
        }

        linkCode.RedeemedAt = DateTime.UtcNow;
        linkCode.RedeemedByDeviceId = device.Id;

        await _db.SaveChangesAsync(cancellationToken);

        var token = _tokenService.CreateDeviceToken(device.Id, device.AccountId);
        _logger.LogInformation("Device {DeviceId} linked to account {AccountId}", device.Id, device.AccountId);

        return Ok(new DeviceTokenResponse { DeviceId = device.Id, Token = token });
    }

    /// <summary>Parent lists their linked devices.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> ListDevices(CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        var devices = await _db.Devices
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.LinkedAt)
            .Select(d => new DeviceDto
            {
                Id = d.Id,
                Name = d.Name,
                ChildProfileId = d.ChildProfileId,
                LinkedAt = d.LinkedAt,
                LastSeenAt = d.LastSeenAt
            })
            .ToListAsync(cancellationToken);

        return Ok(devices);
    }

    /// <summary>Device (or parent) fetches the effective policy for a specific device.</summary>
    [HttpGet("{id:guid}/policy")]
    [Authorize]
    public async Task<ActionResult<PolicyConfiguration>> GetDevicePolicy(Guid id, CancellationToken cancellationToken)
    {
        var accountId = User.GetAccountId();
        if (accountId is null)
        {
            return Unauthorized();
        }

        // A device token may only read its own policy.
        var callerDeviceId = User.GetDeviceId();
        if (callerDeviceId is not null && callerDeviceId != id)
        {
            return Forbid();
        }

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.Id == id && d.AccountId == accountId, cancellationToken);

        if (device is null)
        {
            return NotFound();
        }

        device.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var policy = await _policyStore.GetDevicePolicyAsync(id, accountId, cancellationToken);
        return Ok(policy);
    }

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }
}
