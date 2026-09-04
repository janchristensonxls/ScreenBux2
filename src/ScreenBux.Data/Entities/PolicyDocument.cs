namespace ScreenBux.Data.Entities;

/// <summary>
/// Database-backed policy, scoped to an account and optionally to a child profile or device.
/// The policy payload is stored as serialized <c>PolicyConfiguration</c> JSON so the existing
/// Shared model remains the single source of truth for its shape.
///
/// This is a pointer + cache: <see cref="ActivePolicyProfileId"/> identifies which named
/// <see cref="PolicyProfile"/> ("mode") is currently active for this scope, and
/// <see cref="PolicyJson"/> is a denormalized copy of that profile's content, refreshed
/// whenever the active profile changes or its content is edited. Downstream consumers
/// (the Service, via REST/SignalR) only ever need to read <see cref="PolicyJson"/> - they
/// don't need to know profiles exist.
/// </summary>
public class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public Guid? ChildProfileId { get; set; }

    public Guid? DeviceId { get; set; }

    /// <summary>
    /// The <see cref="PolicyProfile"/> currently active for this scope, if any. Null for
    /// documents predating the profile feature, or for scopes that have never had a mode
    /// explicitly selected (falls back to whatever is in <see cref="PolicyJson"/> as-is).
    /// </summary>
    public Guid? ActivePolicyProfileId { get; set; }

    public PolicyProfile? ActivePolicyProfile { get; set; }

    /// <summary>
    /// Serialized <c>ScreenBux.Shared.Models.PolicyConfiguration</c>.
    /// </summary>
    public string PolicyJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
