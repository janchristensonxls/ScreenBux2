namespace ScreenBux.Data.Entities;

/// <summary>
/// A named, reusable policy variant ("mode") a parent can author once and switch between -
/// e.g. "Normal", "School", "Open", "Sleep". Scoped to an account and, typically, a specific
/// <see cref="ChildProfile"/> (a mode applies to a given child, not the whole account).
/// The rule content is stored as serialized <c>PolicyConfiguration</c> JSON, same shape as
/// <see cref="PolicyDocument"/>, so the Shared model remains the single source of truth.
/// </summary>
public class PolicyProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public Guid? ChildProfileId { get; set; }

    public ChildProfile? ChildProfile { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Marks profiles seeded by default (e.g. "Normal", "School", "Open", "Sleep") so the UI
    /// can distinguish them from parent-authored ones if desired. Not otherwise special-cased -
    /// a built-in profile can be freely edited like any other.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Serialized <c>ScreenBux.Shared.Models.PolicyConfiguration</c>.
    /// </summary>
    public string PolicyJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
