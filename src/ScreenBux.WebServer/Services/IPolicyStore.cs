using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Abstraction over policy persistence. Backed by the database (per account),
/// replacing the previous single-file <c>policy.json</c> source-of-truth on the server.
/// </summary>
public interface IPolicyStore
{
    /// <summary>
    /// Gets the account-level policy. If none exists yet, seeds it from the legacy
    /// <c>policy.json</c> file (when present) so existing rules are not lost.
    /// </summary>
    Task<PolicyConfiguration> GetPolicyAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the account-level policy.</summary>
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

    /// <summary>
    /// Updates an existing profile's content. If the profile is currently active, the cached
    /// effective policy is refreshed too.
    /// </summary>
    Task<PolicyProfileDto?> UpdateProfileAsync(string accountId, Guid profileId, string name, PolicyConfiguration policy, CancellationToken cancellationToken = default);

    /// <summary>Deletes a policy profile. Fails silently (no-op) if it is currently active.</summary>
    Task<bool> DeleteProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the given profile the active one for the account, refreshing the cached effective
    /// policy so downstream consumers (Service via REST/SignalR) see the change immediately.
    /// </summary>
    Task<PolicyConfiguration?> SetActiveProfileAsync(string accountId, Guid profileId, CancellationToken cancellationToken = default);
}
