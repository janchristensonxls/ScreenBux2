using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScreenBux.Data;
using ScreenBux.Data.Entities;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Services;
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
        var device = await GetDeviceBundleAsync(deviceId, accountId, cancellationToken);
        if (device is null)
        {
            // Not a device of this account: the account default, as before.
            return await GetPolicyAsync(accountId, cancellationToken);
        }

        return ModeResolver.ResolvePolicy(device.Bundle, DateTime.UtcNow, ModeResolver.FindTimeZoneOrUtc(device.TimeZoneId)).Policy;
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

        // Don't delete a mode that is in use anywhere: a default (account, child, device), an
        // override, or a schedule entry. Schedules are JSON, so they are checked in memory.
        var documents = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId)
            .ToListAsync(cancellationToken);
        var inUse = documents.Any(d =>
            d.ActivePolicyProfileId == profileId
            || d.OverrideProfileId == profileId
            || ParseSchedule(d.ScheduleJson)?.Any(e => e.ProfileId == profileId) == true);
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
        if (!await _db.PolicyProfiles.AnyAsync(p => p.AccountId == accountId && p.Id == profileId, cancellationToken))
        {
            return null;
        }

        // "Everyone" really means every device: drop all child AND device-scoped mode assignments,
        // since a device-scoped document takes precedence over the default and would otherwise keep
        // that device on its old mode (e.g. outside "Sleep") while the UI reports everyone switched.
        // Temporary overrides (account and child) end too, since they would beat the new default.
        // Schedules are kept: they are standing configuration, not a current-state choice.
        // Saved together with the default's repointing in SetActiveProfileAsync.
        var documents = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            ClearOverride(document);
            if (document.DeviceId != null)
            {
                _db.PolicyDocuments.Remove(document);
            }
            else if (document.ChildProfileId != null)
            {
                document.ActivePolicyProfileId = null;
                RemoveIfEmpty(document);
            }
        }

        return await SetActiveProfileAsync(accountId, profileId, childProfileId: null, cancellationToken);
    }

    public async Task<bool> ClearChildAssignmentAsync(string accountId, Guid childProfileId, CancellationToken cancellationToken = default)
    {
        if (!await _db.ChildProfiles.AnyAsync(c => c.AccountId == accountId && c.Id == childProfileId, cancellationToken))
        {
            return false;
        }

        // Only the mode pointer goes; the child's own schedule/override (same document) stay.
        var documents = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId && d.ChildProfileId == childProfileId && d.DeviceId == null)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            document.ActivePolicyProfileId = null;
            document.UpdatedAt = DateTime.UtcNow;
            RemoveIfEmpty(document);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // --- Mode schedule / temporary override ---

    public async Task<IReadOnlyDictionary<Guid, DeviceBundle>> GetDeviceBundlesAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var data = await LoadSelectionDataAsync(accountId, cancellationToken);
        return data.Devices.ToDictionary(d => d.Id, d => new DeviceBundle(BuildBundle(data, d), d.TimeZoneId));
    }

    public async Task<DeviceBundle?> GetDeviceBundleAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default)
    {
        var data = await LoadSelectionDataAsync(accountId, cancellationToken);
        var device = data.Devices.FirstOrDefault(d => d.Id == deviceId);
        return device is null ? null : new DeviceBundle(BuildBundle(data, device), device.TimeZoneId);
    }

    public async Task<IReadOnlyList<ModeScopeStatusDto>> GetModeStatusAsync(string accountId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var data = await LoadSelectionDataAsync(accountId, cancellationToken);

        // Evaluate each scope in the time zone of one of its devices: for a child, its own
        // devices; for the account default, devices without a child first, then any device.
        string? TimeZoneFor(Guid? childId) =>
            data.Devices.Where(d => d.ChildProfileId == childId).Select(d => d.TimeZoneId).FirstOrDefault(tz => tz != null)
            ?? (childId is null ? data.Devices.Select(d => d.TimeZoneId).FirstOrDefault(tz => tz != null) : null);

        ModeScopeStatusDto Status(Guid? childId, string? displayName)
        {
            var own = childId is Guid c ? data.ChildDocs.GetValueOrDefault(c) : data.AccountDoc;
            var selection = SelectionFor(data, childId, deviceId: null);
            var timeZone = ModeResolver.FindTimeZoneOrUtc(TimeZoneFor(childId));

            return new ModeScopeStatusDto
            {
                ChildProfileId = childId,
                DisplayName = displayName,
                DefaultProfileId = selection.DefaultProfileId,
                OwnSchedule = ParseSchedule(own?.ScheduleJson) ?? (childId is null ? new List<ModeScheduleEntry>() : null),
                EffectiveSchedule = selection.Schedule,
                OwnOverride = ToOverride(own),
                EffectiveOverride = selection.Override,
                TimeZoneId = timeZone.Id,
                Current = ModeResolver.Resolve(selection, utcNow, timeZone, data.Profiles.ContainsKey)
            };
        }

        var result = new List<ModeScopeStatusDto> { Status(null, null) };
        result.AddRange(data.Children.OrderBy(c => c.DisplayName).Select(c => Status(c.Id, c.DisplayName)));
        return result;
    }

    public async Task<ModeUpdateResult> SetScheduleAsync(string accountId, Guid? childProfileId, List<ModeScheduleEntry>? schedule, CancellationToken cancellationToken = default)
    {
        if (schedule is not null && !await ProfilesExistAsync(accountId, schedule.Select(e => e.ProfileId), cancellationToken))
        {
            return ModeUpdateResult.UnknownProfile;
        }

        var document = await GetOrCreateScopeDocumentAsync(accountId, childProfileId, cancellationToken);
        if (document is null)
        {
            return ModeUpdateResult.NotFound;
        }

        // The account scope has nothing to inherit from: null and empty both mean "no schedule".
        document.ScheduleJson = schedule is null || (childProfileId is null && schedule.Count == 0)
            ? null
            : JsonSerializer.Serialize(schedule, SerializerOptions);
        document.UpdatedAt = DateTime.UtcNow;
        RemoveIfEmpty(document);

        await _db.SaveChangesAsync(cancellationToken);
        return ModeUpdateResult.Ok;
    }

    public async Task<ModeUpdateResult> SetOverrideAsync(string accountId, Guid? childProfileId, Guid profileId, DateTime? expiresAtUtc, bool endsAtNextScheduleChange, CancellationToken cancellationToken = default)
    {
        if (!await ProfilesExistAsync(accountId, new[] { profileId }, cancellationToken))
        {
            return ModeUpdateResult.UnknownProfile;
        }

        var document = await GetOrCreateScopeDocumentAsync(accountId, childProfileId, cancellationToken);
        if (document is null)
        {
            return ModeUpdateResult.NotFound;
        }

        ApplyOverride(document, profileId, expiresAtUtc, endsAtNextScheduleChange);
        await _db.SaveChangesAsync(cancellationToken);
        return ModeUpdateResult.Ok;
    }

    public async Task<ModeUpdateResult> SetOverrideForEveryoneAsync(string accountId, Guid profileId, DateTime? expiresAtUtc, bool endsAtNextScheduleChange, CancellationToken cancellationToken = default)
    {
        if (!await ProfilesExistAsync(accountId, new[] { profileId }, cancellationToken))
        {
            return ModeUpdateResult.UnknownProfile;
        }

        var accountDocument = (await GetOrCreateScopeDocumentAsync(accountId, null, cancellationToken))!;
        var childDocuments = await _db.PolicyDocuments
            .Where(d => d.AccountId == accountId && d.ChildProfileId != null && d.DeviceId == null && d.OverrideProfileId != null)
            .ToListAsync(cancellationToken);
        foreach (var document in childDocuments)
        {
            ClearOverride(document);
            RemoveIfEmpty(document);
        }

        ApplyOverride(accountDocument, profileId, expiresAtUtc, endsAtNextScheduleChange);
        await _db.SaveChangesAsync(cancellationToken);
        return ModeUpdateResult.Ok;
    }

    public async Task<ModeUpdateResult> ClearOverrideAsync(string accountId, Guid? childProfileId, CancellationToken cancellationToken = default)
    {
        var document = await GetOrCreateScopeDocumentAsync(accountId, childProfileId, cancellationToken);
        if (document is null)
        {
            return ModeUpdateResult.NotFound;
        }

        ClearOverride(document);
        RemoveIfEmpty(document);
        await _db.SaveChangesAsync(cancellationToken);
        return ModeUpdateResult.Ok;
    }

    private sealed record DeviceRow(Guid Id, Guid? ChildProfileId, string? TimeZoneId);

    /// <summary>Everything needed to build any scope's <see cref="ModeSelection"/>, loaded in one go.</summary>
    private sealed record SelectionData(
        Guid AccountDefaultId,
        Dictionary<Guid, PolicyProfile> Profiles,
        PolicyDocument AccountDoc,
        Dictionary<Guid, PolicyDocument> ChildDocs,
        Dictionary<Guid, Guid> DeviceDefaults,
        List<DeviceRow> Devices,
        List<ChildProfile> Children);

    private async Task<SelectionData> LoadSelectionDataAsync(string accountId, CancellationToken cancellationToken)
    {
        var defaultId = (await GetOrCreateActiveProfileAsync(accountId, cancellationToken)).Id;
        var profiles = await _db.PolicyProfiles.Where(p => p.AccountId == accountId).ToDictionaryAsync(p => p.Id, cancellationToken);
        var documents = await _db.PolicyDocuments.Where(d => d.AccountId == accountId).ToListAsync(cancellationToken);
        var devices = await _db.Devices
            .Where(d => d.AccountId == accountId)
            .Select(d => new DeviceRow(d.Id, d.ChildProfileId, d.TimeZoneId))
            .ToListAsync(cancellationToken);
        var children = await _db.ChildProfiles.Where(c => c.AccountId == accountId).ToListAsync(cancellationToken);

        var accountDoc = documents.First(d => d.ChildProfileId == null && d.DeviceId == null);
        var childDocs = documents
            .Where(d => d.ChildProfileId != null && d.DeviceId == null)
            .GroupBy(d => d.ChildProfileId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var deviceDefaults = documents
            .Where(d => d.DeviceId != null && d.ActivePolicyProfileId is Guid p && profiles.ContainsKey(p))
            .GroupBy(d => d.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ActivePolicyProfileId!.Value);

        return new SelectionData(defaultId, profiles, accountDoc, childDocs, deviceDefaults, devices, children);
    }

    /// <summary>
    /// Flattens the account and child documents into one selection: the child's own default,
    /// schedule and override where set, otherwise the account's. A device-scoped document (legacy)
    /// only overrides the default mode.
    /// </summary>
    private static ModeSelection SelectionFor(SelectionData data, Guid? childId, Guid? deviceId)
    {
        var childDoc = childId is Guid c ? data.ChildDocs.GetValueOrDefault(c) : null;

        var defaultId = deviceId is Guid d && data.DeviceDefaults.TryGetValue(d, out var deviceDefault) ? deviceDefault
            : childDoc?.ActivePolicyProfileId is Guid childDefault && data.Profiles.ContainsKey(childDefault) ? childDefault
            : data.AccountDefaultId;

        // A child's own override wins, but the account-wide one stays in the selection behind it
        // so it resumes when the child's ends (e.g. a short "Open" within an everyone-"Sleep").
        var childOverride = ToOverride(childDoc);
        var accountOverride = ToOverride(data.AccountDoc);

        return new ModeSelection
        {
            DefaultProfileId = defaultId,
            Schedule = (childDoc?.ScheduleJson is not null ? ParseSchedule(childDoc.ScheduleJson) : ParseSchedule(data.AccountDoc.ScheduleJson))
                ?? new List<ModeScheduleEntry>(),
            Override = childOverride ?? accountOverride,
            InheritedOverride = childOverride is null ? null : accountOverride
        };
    }

    private static DevicePolicyBundle BuildBundle(SelectionData data, DeviceRow device)
    {
        var selection = SelectionFor(data, device.ChildProfileId, device.Id);

        var referenced = new HashSet<Guid> { selection.DefaultProfileId };
        referenced.UnionWith(selection.Schedule.Select(e => e.ProfileId));
        foreach (var ov in new[] { selection.Override, selection.InheritedOverride })
        {
            if (ov is not null)
            {
                referenced.Add(ov.ProfileId);
            }
        }

        var profiles = new Dictionary<Guid, PolicyConfiguration>();
        foreach (var id in referenced)
        {
            if (data.Profiles.TryGetValue(id, out var profile))
            {
                var policy = Deserialize(profile.PolicyJson);
                policy.Name = profile.Name;
                profiles[id] = policy;
            }
        }

        return new DevicePolicyBundle { Selection = selection, Profiles = profiles };
    }

    /// <summary>
    /// The account's unscoped document, or a child's document (created with no mode pointer if
    /// missing - the child then still follows the default). Null if the child isn't on the account.
    /// </summary>
    private async Task<PolicyDocument?> GetOrCreateScopeDocumentAsync(string accountId, Guid? childProfileId, CancellationToken cancellationToken)
    {
        if (childProfileId is not Guid childId)
        {
            await GetOrCreateActiveProfileAsync(accountId, cancellationToken);
            return await _db.PolicyDocuments
                .FirstAsync(d => d.AccountId == accountId && d.ChildProfileId == null && d.DeviceId == null, cancellationToken);
        }

        if (!await _db.ChildProfiles.AnyAsync(c => c.AccountId == accountId && c.Id == childId, cancellationToken))
        {
            return null;
        }

        var document = await _db.PolicyDocuments
            .FirstOrDefaultAsync(d => d.AccountId == accountId && d.ChildProfileId == childId && d.DeviceId == null, cancellationToken);
        if (document is null)
        {
            document = new PolicyDocument { AccountId = accountId, ChildProfileId = childId };
            _db.PolicyDocuments.Add(document);
        }

        return document;
    }

    private async Task<bool> ProfilesExistAsync(string accountId, IEnumerable<Guid> profileIds, CancellationToken cancellationToken)
    {
        var ids = profileIds.Distinct().ToList();
        var found = await _db.PolicyProfiles.CountAsync(p => p.AccountId == accountId && ids.Contains(p.Id), cancellationToken);
        return found == ids.Count;
    }

    private static void ApplyOverride(PolicyDocument document, Guid profileId, DateTime? expiresAtUtc, bool endsAtNextScheduleChange)
    {
        document.OverrideProfileId = profileId;
        document.OverrideStartedAtUtc = DateTime.UtcNow;
        document.OverrideExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        document.OverrideEndsAtScheduleChange = endsAtNextScheduleChange;
        document.UpdatedAt = DateTime.UtcNow;
    }

    private static void ClearOverride(PolicyDocument document)
    {
        document.OverrideProfileId = null;
        document.OverrideStartedAtUtc = null;
        document.OverrideExpiresAtUtc = null;
        document.OverrideEndsAtScheduleChange = false;
        document.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>A child/device document with no pointer, schedule or override carries nothing - delete it.</summary>
    private void RemoveIfEmpty(PolicyDocument document)
    {
        var scoped = document.ChildProfileId != null || document.DeviceId != null;
        if (scoped && document.ActivePolicyProfileId is null && document.OverrideProfileId is null && document.ScheduleJson is null)
        {
            if (_db.Entry(document).State == EntityState.Added)
            {
                _db.Entry(document).State = EntityState.Detached;
            }
            else
            {
                _db.PolicyDocuments.Remove(document);
            }
        }
    }

    private static ModeOverride? ToOverride(PolicyDocument? document) =>
        document?.OverrideProfileId is Guid profileId
            ? new ModeOverride
            {
                ProfileId = profileId,
                // SQL Server returns Unspecified kinds; these columns are always UTC.
                StartedAtUtc = DateTime.SpecifyKind(document.OverrideStartedAtUtc ?? document.UpdatedAt, DateTimeKind.Utc),
                ExpiresAtUtc = document.OverrideExpiresAtUtc is DateTime e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
                EndsAtNextScheduleChange = document.OverrideEndsAtScheduleChange
            }
            : null;

    private static List<ModeScheduleEntry>? ParseSchedule(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<List<ModeScheduleEntry>>(json, SerializerOptions) ?? new List<ModeScheduleEntry>();

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
