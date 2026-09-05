namespace ScreenBux.Data.Entities;

/// <summary>
/// A temporary, per-device "pause all enforcement" window a parent can grant (and
/// add/subtract/set/clear at any time), independent of whatever <see cref="PolicyProfile"/>
/// is currently active. Represented as an absolute expiry timestamp rather than a
/// decrementing counter, so "is a grant active" (<c>ExpiresAtUtc > DateTime.UtcNow</c>) is a
/// stateless comparison every reader (WebServer, Service, WebClient) can evaluate
/// independently without any coordination. One row per device; a null/past
/// <see cref="ExpiresAtUtc"/> means no active grant.
/// </summary>
public class DeviceGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    /// <summary>
    /// When the grant expires (UTC). Null or in the past means enforcement is not paused.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
