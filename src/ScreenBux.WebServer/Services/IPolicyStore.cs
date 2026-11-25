using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Abstraction over policy persistence. Backed by the database (per account). Policy JSON is
/// stored only on <c>PolicyProfile</c> rows; the account's <c>PolicyDocument</c> is a pure
/// pointer to whichever profile is currently active, so there is a single source of truth for
/// policy content - no separate cache that can drift out of sync.
/// </summary>
public interface IPolicyStore
{
    /// <summary>
    /// Gets the policy of the account's currently active profile. If the account has no active
    /// profile yet, a default "Normal" profile is created and seeded from the legacy
    /// <c>policy.json</c> file (when present) so existing rules are not lost.
    /// </summary>
    Task<PolicyConfiguration> GetPolicyAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the policy content of the account's currently active profile (creating a default
    /// active profile first if none exists). This is the raw JSON editor's save path.
    /// </summary>
    Task SavePolicyAsync(string accountId, PolicyConfiguration policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the policy in effect right now for a specific device - default, schedule and override
    /// resolved in the device's reported time zone. For clients that don't understand
    /// <see cref="DevicePolicyBundle"/>.
    /// </summary>
    Task<PolicyConfiguration> GetDevicePolicyAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the account-wide policy profiles ("modes"). Seeds a default set (Normal, School,
    /// Open, Sleep) on first access if none exist yet.
    /// </summary>
    Task<IReadOnlyList<PolicyProfileDto>> GetProfilesAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new policy profile for the account.</summary>
    Task<PolicyProfileDto> CreateProfileAsync(string accountId, string name, PolicyConfiguration policy, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing profile's content.</summary>
    Task<PolicyProfileDto?> UpdateProfileAsync(string accountId, Guid profileId, string name, PolicyConfiguration policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a policy profile. Returns false (no-op) if it is in use anywhere: as a default,
    /// in a schedule, or by an override.
    /// </summary>
    Task<bool> DeleteProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the given profile the active one for the account by repointing the account's
    /// <c>PolicyDocument</c> at it, so downstream consumers (Service via REST/SignalR) see the
    /// change immediately.
    /// </summary>
    /// <summary>Activates a mode for one child, or (childProfileId null) as the account default.</summary>
    Task<PolicyConfiguration?> SetActiveProfileAsync(string accountId, Guid profileId, Guid? childProfileId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a mode the account default and removes every child and device override, so every
    /// device of the account ends up on it.
    /// </summary>
    Task<PolicyConfiguration?> SetActiveProfileForEveryoneAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one child's own mode assignment so the child follows the account default again.
    /// Returns false if the child doesn't belong to the account.
    /// </summary>
    Task<bool> ClearChildAssignmentAsync(string accountId, Guid childProfileId, CancellationToken cancellationToken = default);

    Task<PolicyAssignmentsDto> GetAssignmentsAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Default mode id per linked device of the account (before schedule/override).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetDeviceProfileMapAsync(string accountId, CancellationToken cancellationToken = default);

    // --- Mode schedule / temporary override (docs/decisions/mode-scheduling-and-temporary-overrides.md) ---

    /// <summary>
    /// The <see cref="DevicePolicyBundle"/> of every linked device of the account, plus each
    /// device's reported time zone. Used to serve devices and to detect which devices need a push.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DeviceBundle>> GetDeviceBundlesAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>One device's bundle; null if the device doesn't belong to the account.</summary>
    Task<DeviceBundle?> GetDeviceBundleAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default);

    /// <summary>Configured and current mode for the account default and every child.</summary>
    Task<IReadOnlyList<ModeScopeStatusDto>> GetModeStatusAsync(string accountId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the schedule of the account default (<paramref name="childProfileId"/> null) or a
    /// child. For a child, null means "follow the account schedule".
    /// </summary>
    Task<ModeUpdateResult> SetScheduleAsync(string accountId, Guid? childProfileId, List<ModeScheduleEntry>? schedule, CancellationToken cancellationToken = default);

    /// <summary>Sets (replaces) the temporary override of a scope, starting now.</summary>
    Task<ModeUpdateResult> SetOverrideAsync(string accountId, Guid? childProfileId, Guid profileId, DateTime? expiresAtUtc, bool endsAtNextScheduleChange, CancellationToken cancellationToken = default);

    /// <summary>Sets the account override and removes every child override, so it reaches everyone.</summary>
    Task<ModeUpdateResult> SetOverrideForEveryoneAsync(string accountId, Guid profileId, DateTime? expiresAtUtc, bool endsAtNextScheduleChange, CancellationToken cancellationToken = default);

    /// <summary>Ends a scope's override now ("End now").</summary>
    Task<ModeUpdateResult> ClearOverrideAsync(string accountId, Guid? childProfileId, CancellationToken cancellationToken = default);
}

/// <summary>A device's bundle together with the time zone it last reported (null if never).</summary>
public sealed record DeviceBundle(DevicePolicyBundle Bundle, string? TimeZoneId);

public enum ModeUpdateResult
{
    Ok,
    /// <summary>The child profile doesn't exist on the account.</summary>
    NotFound,
    /// <summary>A referenced mode doesn't exist on the account.</summary>
    UnknownProfile
}
