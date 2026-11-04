namespace ScreenBux.Shared.Models;

/// <summary>
/// A named policy profile ("mode") a parent can author and switch between - e.g. "Normal",
/// "School", "Open", "Sleep". Used by the WebServer API/WebClient UI; the Service continues to
/// consume only the resolved effective <see cref="PolicyConfiguration"/>.
/// </summary>
public class PolicyProfileDto
{
    public Guid Id { get; set; }

    public Guid? ChildProfileId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    /// <summary>True if this mode is in use anywhere: as the account default or by any child.</summary>
    public bool IsActive { get; set; }

    /// <summary>True if this is the account-wide default mode (used by unassigned devices and by children without their own mode).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Children whose effective mode is this one (explicitly assigned, or following the default).</summary>
    public List<Guid> UsedByChildIds { get; set; } = new();

    public PolicyConfiguration Policy { get; set; } = new();

    public DateTime UpdatedAt { get; set; }
}
