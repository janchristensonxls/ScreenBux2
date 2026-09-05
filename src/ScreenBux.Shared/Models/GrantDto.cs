namespace ScreenBux.Shared.Models;

/// <summary>
/// A device's current time-grant status: while <see cref="ExpiresAtUtc"/> is in the future,
/// ALL enforcement on that device (regular process rules and Always/power-action rules alike)
/// is paused, independent of whatever policy profile/mode is currently active. Used across the
/// REST API (WebServer), the SignalR "GrantUpdated" broadcast, the Service's local cache, and
/// the Agent's named-pipe status query - one shape, four transports.
/// </summary>
public class GrantDto
{
    public Guid DeviceId { get; set; }

    /// <summary>
    /// When the grant expires (UTC). Null, or a value in the past, means no active grant -
    /// enforcement runs normally. "Is a grant active" is always just
    /// <c>ExpiresAtUtc > DateTime.UtcNow</c>; there is no separate flag to keep in sync.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }
}
