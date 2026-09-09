using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models.Devices;

namespace ScreenBux.WebServer.Services;

/// <summary>EF Core-backed <see cref="IChildProfileStore"/>.</summary>
public class EfChildProfileStore : IChildProfileStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<EfChildProfileStore> _logger;

    public EfChildProfileStore(AppDbContext db, ILogger<EfChildProfileStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChildProfileDto>> GetProfilesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        return await _db.ChildProfiles
            .Where(c => c.AccountId == accountId)
            .OrderBy(c => c.DisplayName)
            .Select(c => new ChildProfileDto
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                DeviceCount = c.Devices.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ChildProfileDto> CreateProfileAsync(string accountId, string displayName, CancellationToken cancellationToken = default)
    {
        var profile = new ChildProfile
        {
            AccountId = accountId,
            DisplayName = displayName
        };

        _db.ChildProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created child profile {ChildProfileId} for account {AccountId}", profile.Id, accountId);

        return new ChildProfileDto { Id = profile.Id, DisplayName = profile.DisplayName, DeviceCount = 0 };
    }

    public async Task<ChildProfileDto?> UpdateProfileAsync(string accountId, Guid childProfileId, string displayName, CancellationToken cancellationToken = default)
    {
        var profile = await _db.ChildProfiles
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Id == childProfileId, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        profile.DisplayName = displayName;
        await _db.SaveChangesAsync(cancellationToken);

        var deviceCount = await _db.Devices.CountAsync(d => d.ChildProfileId == childProfileId, cancellationToken);

        return new ChildProfileDto { Id = profile.Id, DisplayName = profile.DisplayName, DeviceCount = deviceCount };
    }

    public async Task<bool> DeleteProfileAsync(string accountId, Guid childProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await _db.ChildProfiles
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Id == childProfileId, cancellationToken);

        if (profile is null)
        {
            return false;
        }

        var hasDevices = await _db.Devices.AnyAsync(d => d.ChildProfileId == childProfileId, cancellationToken);
        if (hasDevices)
        {
            return false;
        }

        _db.ChildProfiles.Remove(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
