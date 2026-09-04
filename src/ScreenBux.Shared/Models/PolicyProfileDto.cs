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

    public bool IsActive { get; set; }

    public PolicyConfiguration Policy { get; set; } = new();

    public DateTime UpdatedAt { get; set; }
}
