using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// EF Core-backed <see cref="IPolicyStore"/>. Stores each account's policy as serialized
/// <see cref="PolicyConfiguration"/> JSON in a <see cref="PolicyDocument"/> row.
/// </summary>
public class EfPolicyStore : IPolicyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EfPolicyStore> _logger;

    public EfPolicyStore(AppDbContext db, IConfiguration configuration, ILogger<EfPolicyStore> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PolicyConfiguration> GetPolicyAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is not null)
        {
            return Deserialize(document.PolicyJson);
        }

        // First access: seed from legacy policy.json when available so nothing is lost.
        var seeded = LoadLegacyPolicyOrDefault();
        await SavePolicyAsync(accountId, seeded, cancellationToken);
        return seeded;
    }

    public async Task SavePolicyAsync(string accountId, PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        var json = JsonSerializer.Serialize(policy, SerializerOptions);

        if (document is null)
        {
            document = new PolicyDocument
            {
                AccountId = accountId,
                PolicyJson = json,
                UpdatedAt = DateTime.UtcNow
            };
            _db.PolicyDocuments.Add(document);
        }
        else
        {
            document.PolicyJson = json;
            document.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PolicyConfiguration> GetDevicePolicyAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default)
    {
        var deviceDoc = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.DeviceId == deviceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (deviceDoc is not null)
        {
            return Deserialize(deviceDoc.PolicyJson);
        }

        return await GetPolicyAsync(accountId, cancellationToken);
    }

    public async Task<IReadOnlyList<PolicyProfileDto>> GetProfilesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var profiles = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            profiles = await SeedDefaultProfilesAsync(accountId, cancellationToken);
        }

        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);
        var activeProfileId = document?.ActivePolicyProfileId;

        return profiles
            .OrderBy(p => p.Name)
            .Select(p => ToDto(p, p.Id == activeProfileId))
            .ToList();
    }

    public async Task<PolicyProfileDto> CreateProfileAsync(string accountId, string name, PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        var profile = new PolicyProfile
        {
            AccountId = accountId,
            Name = name,
            PolicyJson = JsonSerializer.Serialize(policy, SerializerOptions),
            UpdatedAt = DateTime.UtcNow
        };

        _db.PolicyProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(profile, isActive: false);
    }

    public async Task<PolicyProfileDto?> UpdateProfileAsync(string accountId, Guid profileId, string name, PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        var profile = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId && p.Id == profileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        profile.Name = name;
        profile.PolicyJson = JsonSerializer.Serialize(policy, SerializerOptions);
        profile.UpdatedAt = DateTime.UtcNow;

        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        var isActive = document?.ActivePolicyProfileId == profileId;
        if (isActive && document is not null)
        {
            document.PolicyJson = profile.PolicyJson;
            document.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(profile, isActive);
    }

    public async Task<bool> DeleteProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId && p.Id == profileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return false;
        }

        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document?.ActivePolicyProfileId == profileId)
        {
            // Don't delete the currently active profile.
            return false;
        }

        _db.PolicyProfiles.Remove(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PolicyConfiguration?> SetActiveProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId && p.Id == profileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            document = new PolicyDocument
            {
                AccountId = accountId
            };
            _db.PolicyDocuments.Add(document);
        }

        document.ActivePolicyProfileId = profile.Id;
        document.PolicyJson = profile.PolicyJson;
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Deserialize(document.PolicyJson);
    }

    private async Task<List<PolicyProfile>> SeedDefaultProfilesAsync(string accountId, CancellationToken cancellationToken)
    {
        var normal = new PolicyConfiguration { EnableMonitoring = true, CheckIntervalSeconds = 5, LogActivity = true };
        var open = new PolicyConfiguration { EnableMonitoring = true, CheckIntervalSeconds = 5, LogActivity = true };
        var school = new PolicyConfiguration { EnableMonitoring = true, CheckIntervalSeconds = 5, LogActivity = true };
        var sleep = new PolicyConfiguration
        {
            EnableMonitoring = true,
            CheckIntervalSeconds = 5,
            LogActivity = true,
            Rules = new List<PolicyRule>
            {
                new PolicyRule
                {
                    Name = "Lockout",
                    ConditionKind = PolicyConditionKind.Always,
                    Action = PolicyRuleAction.Sleep,
                    Enabled = true
                }
            }
        };

        var profiles = new List<PolicyProfile>
        {
            new() { AccountId = accountId, Name = "Normal", IsBuiltIn = true, PolicyJson = JsonSerializer.Serialize(normal, SerializerOptions) },
            new() { AccountId = accountId, Name = "Open", IsBuiltIn = true, PolicyJson = JsonSerializer.Serialize(open, SerializerOptions) },
            new() { AccountId = accountId, Name = "School", IsBuiltIn = true, PolicyJson = JsonSerializer.Serialize(school, SerializerOptions) },
            new() { AccountId = accountId, Name = "Sleep", IsBuiltIn = true, PolicyJson = JsonSerializer.Serialize(sleep, SerializerOptions) }
        };

        _db.PolicyProfiles.AddRange(profiles);
        await _db.SaveChangesAsync(cancellationToken);
        return profiles;
    }

    private static PolicyProfileDto ToDto(PolicyProfile profile, bool isActive) => new()
    {
        Id = profile.Id,
        ChildProfileId = profile.ChildProfileId,
        Name = profile.Name,
        IsBuiltIn = profile.IsBuiltIn,
        IsActive = isActive,
        Policy = Deserialize(profile.PolicyJson),
        UpdatedAt = profile.UpdatedAt
    };


    private PolicyConfiguration LoadLegacyPolicyOrDefault()
    {
        try
        {
            var policyPath = _configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
            if (File.Exists(policyPath))
            {
                var json = File.ReadAllText(policyPath);
                var legacy = Deserialize(json);
                _logger.LogInformation("Seeded policy from legacy file {Path}", policyPath);
                return legacy;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed policy from legacy policy.json; using defaults.");
        }

        return new PolicyConfiguration();
    }

    private static PolicyConfiguration Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PolicyConfiguration();
        }

        return JsonSerializer.Deserialize<PolicyConfiguration>(json) ?? new PolicyConfiguration();
    }
}
