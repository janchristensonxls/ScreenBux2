using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScreenBux.WebServer.Contracts;
using ScreenBux.WebServer.Data;
using ScreenBux.WebServer.Services;

namespace ScreenBux.WebServer.Controllers;

[ApiController]
[Route("api/device")]
public class DeviceAuthController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IDeviceSecretProtector _secretProtector;
    private readonly IDeviceNonceStore _nonceStore;
    private readonly IDeviceTokenService _tokenService;
    private readonly ILogger<DeviceAuthController> _logger;

    public DeviceAuthController(
        AppDbContext dbContext,
        IDeviceSecretProtector secretProtector,
        IDeviceNonceStore nonceStore,
        IDeviceTokenService tokenService,
        ILogger<DeviceAuthController> logger)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _nonceStore = nonceStore;
        _tokenService = tokenService;
        _logger = logger;
    }

    [HttpPost("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<DeviceAuthResponse>> Authenticate([FromBody] DeviceAuthRequest request)
    {
        if (!IsTimestampFresh(request.Timestamp))
        {
            return Unauthorized(new { message = "Timestamp is not within allowed window." });
        }

        var device = await _dbContext.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId && !d.IsRevoked);

        if (device == null)
        {
            return Unauthorized(new { message = "Device not found." });
        }

        var secret = _secretProtector.Unprotect(device.EncryptedSecret);
        var expectedSignature = ComputeSignature(secret, request.DeviceId, request.ClientType, request.Timestamp, request.Nonce);
        var providedSignature = request.Signature.ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(providedSignature)))
        {
            return Unauthorized(new { message = "Invalid signature." });
        }

        var nonceExpires = request.Timestamp.AddMinutes(10);
        if (!_nonceStore.TryRegisterNonce(device.DeviceId, request.Nonce, nonceExpires))
        {
            return Unauthorized(new { message = "Nonce already used." });
        }

        device.LastSeenAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var token = _tokenService.CreateDeviceToken(device.DeviceId, device.HouseholdId, request.ClientType);

        _logger.LogInformation(
            "Issued device token for {DeviceId} ({ClientType}) at {Timestamp}",
            device.DeviceId,
            request.ClientType,
            DateTime.UtcNow);

        return Ok(new DeviceAuthResponse(token.Token, token.ExpiresAt));
    }

    private static bool IsTimestampFresh(DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        return timestamp >= now.AddMinutes(-5) && timestamp <= now.AddMinutes(5);
    }

    private static string ComputeSignature(string secret, Guid deviceId, string clientType, DateTime timestamp, string nonce)
    {
        var payload = $"{deviceId}|{clientType}|{timestamp:O}|{nonce}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
