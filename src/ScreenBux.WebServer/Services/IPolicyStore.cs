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
}
