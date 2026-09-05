using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// EF Core-backed <see cref="IGrantStore"/>. One <see cref="DeviceGrant"/> row per device,
/// created lazily on first write.
/// </summary>
public class EfGrantStore : IGrantStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<EfGrantStore> _logger;

    public EfGrantStore(AppDbContext db, ILogger<EfGrantStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<GrantDto> GetGrantAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default)
    {
        var grant = await FindGrantAsync(deviceId, accountId, cancellationToken);
        return ToDto(deviceId, grant);
    }

    public async Task<GrantDto> SetGrantAsync(Guid deviceId, string accountId, DateTime? expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var grant = await GetOrCreateGrantAsync(deviceId, accountId, cancellationToken);

        grant.ExpiresAtUtc = expiresAtUtc;
        grant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Set grant for device {DeviceId} to expire at {ExpiresAtUtc}", deviceId, expiresAtUtc);

        return ToDto(deviceId, grant);
    }

    public async Task<GrantDto> AddMinutesAsync(Guid deviceId, string accountId, int minutes, CancellationToken cancellationToken = default)
    {
        var grant = await GetOrCreateGrantAsync(deviceId, accountId, cancellationToken);

        var now = DateTime.UtcNow;
        var baseline = grant.ExpiresAtUtc is DateTime existing && existing > now ? existing : now;
        var updated = baseline.AddMinutes(minutes);

        grant.ExpiresAtUtc = updated > now ? updated : null;
        grant.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Adjusted grant for device {DeviceId} by {Minutes} minutes; now expires at {ExpiresAtUtc}", deviceId, minutes, grant.ExpiresAtUtc);

        return ToDto(deviceId, grant);
    }

    private async Task<DeviceGrant?> FindGrantAsync(Guid deviceId, string accountId, CancellationToken cancellationToken)
    {
        return await _db.DeviceGrants
            .Where(g => g.DeviceId == deviceId && g.Device!.AccountId == accountId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<DeviceGrant> GetOrCreateGrantAsync(Guid deviceId, string accountId, CancellationToken cancellationToken)
    {
        var grant = await FindGrantAsync(deviceId, accountId, cancellationToken);
        if (grant is not null)
        {
            return grant;
        }

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == deviceId && d.AccountId == accountId, cancellationToken);
        if (!deviceExists)
        {
            throw new InvalidOperationException($"Device {deviceId} was not found for account {accountId}.");
        }

        grant = new DeviceGrant { DeviceId = deviceId };
        _db.DeviceGrants.Add(grant);
        return grant;
    }

    private static GrantDto ToDto(Guid deviceId, DeviceGrant? grant)
    {
        return new GrantDto
        {
            DeviceId = deviceId,
            ExpiresAtUtc = grant?.ExpiresAtUtc,
        };
    }
}
