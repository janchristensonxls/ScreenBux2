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

    /// <summary>Gets the effective policy for a specific device (falls back to account-level).</summary>
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

    /// <summary>Deletes a policy profile. Fails silently (no-op) if it is currently active.</summary>
    Task<bool> DeleteProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the given profile the active one for the account by repointing the account's
    /// <c>PolicyDocument</c> at it, so downstream consumers (Service via REST/SignalR) see the
    /// change immediately.
    /// </summary>
    Task<PolicyConfiguration?> SetActiveProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);
}
