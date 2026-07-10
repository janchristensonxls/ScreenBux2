namespace ScreenBux.Data.Entities;

/// <summary>
/// Database-backed policy, scoped to an account and optionally to a child profile or device.
/// The policy payload is stored as serialized <c>PolicyConfiguration</c> JSON so the existing
/// Shared model remains the single source of truth for its shape.
/// </summary>
public class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public Guid? ChildProfileId { get; set; }

    public Guid? DeviceId { get; set; }

    /// <summary>
    /// Serialized <c>ScreenBux.Shared.Models.PolicyConfiguration</c>.
    /// </summary>
    public string PolicyJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
