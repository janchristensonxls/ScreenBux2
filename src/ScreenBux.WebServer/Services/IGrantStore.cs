using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Persistence for per-device time grants. WebServer is the sole authoritative writer; the
/// Service and WebClient only read/observe (via REST and the "GrantUpdated" SignalR event).
/// </summary>
public interface IGrantStore
{
    /// <summary>
    /// Gets the current grant status for a device. Returns a <see cref="GrantDto"/> with a null
    /// <see cref="GrantDto.ExpiresAtUtc"/> if no grant row exists yet.
    /// </summary>
    Task<GrantDto> GetGrantAsync(Guid deviceId, string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the absolute expiry for a device's grant. Passing null (or a value in the past)
    /// clears/ends the grant.
    /// </summary>
    Task<GrantDto> SetGrantAsync(Guid deviceId, string accountId, DateTime? expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds (or subtracts, for negative values) minutes to a device's grant, extending from "now"
    /// if there is no active grant, or from the current expiry otherwise. The result is clamped
    /// so it never goes below "now" (i.e. it clears the grant instead of going negative).
    /// </summary>
    Task<GrantDto> AddMinutesAsync(Guid deviceId, string accountId, int minutes, CancellationToken cancellationToken = default);
}
