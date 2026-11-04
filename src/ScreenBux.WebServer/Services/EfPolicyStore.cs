using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// EF Core-backed <see cref="IPolicyStore"/>. Modes (<see cref="PolicyProfile"/>) are shared
/// across the account. Each account has one unscoped <see cref="PolicyDocument"/> pointing at the
/// default mode, plus optionally one per child (<see cref="PolicyDocument.ChildProfileId"/>)
/// pointing at that child's own mode; a child without one follows the default. <see cref="PolicyProfile.PolicyJson"/> is the *only* place policy JSON is stored -
/// the raw "Save policy" editor, per-profile edits, and profile activation all read/write
/// through the single active profile row, so there is no separate cache that can drift out of
/// sync.
/// </summary>
public class EfPolicyStore : IPolicyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = PolicyJsonOptions.Default;

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
        var profile = await GetOrCreateActiveProfileAsync(accountId, cancellationToken);
        var policy = Deserialize(profile.PolicyJson);
        policy.Name = profile.Name;
        return policy;
    }

    public async Task SavePolicyAsync(string accountId, PolicyConfiguration policy, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateActiveProfileAsync(accountId, cancellationToken);

        profile.PolicyJson = JsonSerializer.Serialize(policy, SerializerOptions);
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PolicyConfiguration> GetDevicePolicyAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default)
    {
        var map = await GetDeviceProfileMapAsync(accountId, cancellationToken);
        var defaultProfile = await GetOrCreateActiveProfileAsync(accountId, cancellationToken);
        var profileId = map.TryGetValue(deviceId, out var id) ? id : defaultProfile.Id;

        var profile = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId && p.Id == profileId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? defaultProfile;

        var policy = Deserialize(profile.PolicyJson);
        policy.Name = profile.Name;
        return policy;
    }

    /// <summary>
    /// Effective mode per linked device, resolved most-specific first: a device-scoped
    /// <see cref="PolicyDocument"/>, then the device's child's document, then the account default.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetDeviceProfileMapAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var defaultId = (await GetOrCreateActiveProfileAsync(accountId, cancellationToken)).Id;
        var existingProfileIds = await _db.PolicyProfiles.Where(p => p.AccountId == accountId).Select(p => p.Id).ToListAsync(cancellationToken);
        var documents = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId && d.ActivePolicyProfileId != null)
            .ToListAsync(cancellationToken);
        var devices = await _db.Devices
            .Where(d => d.AccountId == accountId)
            .Select(d => new { d.Id, d.ChildProfileId })
            .ToListAsync(cancellationToken);

        var byDevice = documents
            .Where(d => d.DeviceId != null && existingProfileIds.Contains(d.ActivePolicyProfileId!.Value))
            .GroupBy(d => d.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ActivePolicyProfileId!.Value);
        var byChild = ChildAssignments(documents, existingProfileIds);

        return devices.ToDictionary(
            d => d.Id,
            d => byDevice.TryGetValue(d.Id, out var deviceProfile) ? deviceProfile
                : d.ChildProfileId is Guid childId && byChild.TryGetValue(childId, out var childProfile) ? childProfile
                : defaultId);
    }

    public async Task<PolicyAssignmentsDto> GetAssignmentsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var state = await LoadAssignmentStateAsync(accountId, cancellationToken);
        var deviceCounts = await _db.Devices
            .Where(d => d.AccountId == accountId)
            .GroupBy(d => d.ChildProfileId)
            .Select(g => new { ChildProfileId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new PolicyAssignmentsDto
        {
            DefaultProfileId = state.DefaultProfileId,
            UnassignedDeviceCount = deviceCounts.FirstOrDefault(c => c.ChildProfileId == null)?.Count ?? 0,
            Children = state.Children
                .OrderBy(c => c.DisplayName)
                .Select(c => new ChildModeAssignmentDto
                {
                    ChildProfileId = c.Id,
                    DisplayName = c.DisplayName,
                    DeviceCount = deviceCounts.FirstOrDefault(x => x.ChildProfileId == c.Id)?.Count ?? 0,
                    AssignedProfileId = state.ChildAssigned.TryGetValue(c.Id, out var assigned) ? assigned : null,
                    EffectiveProfileId = state.EffectiveFor(c.Id)
                })
                .ToList()
        };
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

        var state = await LoadAssignmentStateAsync(accountId, cancellationToken);

        return profiles
            .OrderBy(p => p.Name)
            .Select(p => ToDto(p, state))
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

        return ToDto(profile, await LoadAssignmentStateAsync(accountId, cancellationToken));
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

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(profile, await LoadAssignmentStateAsync(accountId, cancellationToken));
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

        // Don't delete a mode that is in use anywhere (the default, a child, or a device).
        var inUse = await _db.PolicyDocuments
            .AnyAsync(d => d.AccountId == accountId && d.ActivePolicyProfileId == profileId, cancellationToken);
        if (inUse)
        {
            return false;
        }

        _db.PolicyProfiles.Remove(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PolicyConfiguration?> SetActiveProfileAsync(string accountId, Guid profileId, Guid? childProfileId = null, CancellationToken cancellationToken = default)
    {
        var profile = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId && p.Id == profileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        if (childProfileId is Guid childId
            && !await _db.ChildProfiles.AnyAsync(c => c.AccountId == accountId && c.Id == childId, cancellationToken))
        {
            return null;
        }

        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == childProfileId && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            document = new PolicyDocument
            {
                AccountId = accountId,
                ChildProfileId = childProfileId
            };
            _db.PolicyDocuments.Add(document);
        }

        document.ActivePolicyProfileId = profile.Id;
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var policy = Deserialize(profile.PolicyJson);
        policy.Name = profile.Name;
        return policy;
    }

    public async Task<PolicyConfiguration?> SetActiveProfileForEveryoneAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default)
    {
        var policy = await SetActiveProfileAsync(accountId, profileId, childProfileId: null, cancellationToken);
        if (policy is null)
        {
            return null;
        }

        // Children go back to following the default. Device-scoped documents are left alone.
        var childDocuments = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId && d.ChildProfileId != null && d.DeviceId == null)
            .ToListAsync(cancellationToken);
        _db.PolicyDocuments.RemoveRange(childDocuments);
        await _db.SaveChangesAsync(cancellationToken);

        return policy;
    }

    private sealed record AssignmentState(Guid DefaultProfileId, Dictionary<Guid, Guid> ChildAssigned, List<ChildProfile> Children)
    {
        public Guid EffectiveFor(Guid childId) => ChildAssigned.TryGetValue(childId, out var id) ? id : DefaultProfileId;
    }

    private async Task<AssignmentState> LoadAssignmentStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var defaultId = (await GetOrCreateActiveProfileAsync(accountId, cancellationToken)).Id;
        var existingProfileIds = await _db.PolicyProfiles.Where(p => p.AccountId == accountId).Select(p => p.Id).ToListAsync(cancellationToken);
        var documents = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId && d.ChildProfileId != null && d.DeviceId == null && d.ActivePolicyProfileId != null)
            .ToListAsync(cancellationToken);
        var children = await _db.ChildProfiles.Where(c => c.AccountId == accountId).ToListAsync(cancellationToken);

        return new AssignmentState(defaultId, ChildAssignments(documents, existingProfileIds), children);
    }

    private static Dictionary<Guid, Guid> ChildAssignments(IEnumerable<PolicyDocument> documents, ICollection<Guid> existingProfileIds) =>
        documents
            .Where(d => d.ChildProfileId != null && d.DeviceId == null && d.ActivePolicyProfileId is Guid p && existingProfileIds.Contains(p))
            .GroupBy(d => d.ChildProfileId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ActivePolicyProfileId!.Value);

    /// <summary>
    /// Resolves the <see cref="PolicyProfile"/> that is currently active for the account's
    /// unscoped <see cref="PolicyDocument"/>, creating a default "Normal" profile and its
    /// pointing document if the account has never had one (first access, or an account that
    /// predates the profile feature). This is the single entry point every read/write path
    /// goes through, so there is exactly one profile row being edited at any time.
    /// </summary>
    private async Task<PolicyProfile> GetOrCreateActiveProfileAsync(string accountId, CancellationToken cancellationToken)
    {
        var document = await _db.PolicyDocuments
            .Where(p => p.AccountId == accountId && p.ChildProfileId == null && p.DeviceId == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (document?.ActivePolicyProfileId is Guid activeProfileId)
        {
            var activeProfile = await _db.PolicyProfiles
                .Where(p => p.AccountId == accountId && p.Id == activeProfileId)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeProfile is not null)
            {
                return activeProfile;
            }
        }

        // No document, or it points at a profile that no longer exists: seed defaults (or
        // reuse existing profiles if any), then activate the first "Normal" one.
        var profiles = await _db.PolicyProfiles
            .Where(p => p.AccountId == accountId)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            profiles = await SeedDefaultProfilesAsync(accountId, cancellationToken);
        }

        var defaultProfile = profiles.FirstOrDefault(p => p.Name == "Normal") ?? profiles[0];

        if (document is null)
        {
            document = new PolicyDocument
            {
                AccountId = accountId
            };
            _db.PolicyDocuments.Add(document);
        }

        document.ActivePolicyProfileId = defaultProfile.Id;
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return defaultProfile;
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
            SessionRules = new List<SessionRule>
            {
                new SessionRule
                {
                    Name = "Lockout",
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

    private static PolicyProfileDto ToDto(PolicyProfile profile, AssignmentState state)
    {
        var policy = Deserialize(profile.PolicyJson);
        policy.Name = profile.Name;
        var usedBy = state.Children.Where(c => state.EffectiveFor(c.Id) == profile.Id).Select(c => c.Id).ToList();
        var isDefault = state.DefaultProfileId == profile.Id;
        return new()
        {
            Id = profile.Id,
            ChildProfileId = profile.ChildProfileId,
            Name = profile.Name,
            IsBuiltIn = profile.IsBuiltIn,
            IsDefault = isDefault,
            IsActive = isDefault || usedBy.Count > 0,
            UsedByChildIds = usedBy,
            Policy = policy,
            UpdatedAt = profile.UpdatedAt
        };
    }

    private static PolicyConfiguration Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PolicyConfiguration();
        }

        return JsonSerializer.Deserialize<PolicyConfiguration>(json, SerializerOptions) ?? new PolicyConfiguration();
    }
}
