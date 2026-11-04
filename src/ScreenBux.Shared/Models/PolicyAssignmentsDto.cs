namespace ScreenBux.Shared.Models;

/// <summary>
/// Which mode (<see cref="PolicyProfileDto"/>) applies to whom. Modes are shared across the
/// account; each child either has its own assigned mode or follows the account default.
/// Devices not linked to any child always use the default.
/// </summary>
public class PolicyAssignmentsDto
{
    public Guid DefaultProfileId { get; set; }

    public List<ChildModeAssignmentDto> Children { get; set; } = new();

    /// <summary>Linked devices with no child profile - they always use the default mode.</summary>
    public int UnassignedDeviceCount { get; set; }
}

public class ChildModeAssignmentDto
{
    public Guid ChildProfileId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public int DeviceCount { get; set; }

    /// <summary>The child's own mode; null when the child follows the account default.</summary>
    public Guid? AssignedProfileId { get; set; }

    /// <summary>The mode actually in effect for the child's devices.</summary>
    public Guid EffectiveProfileId { get; set; }
}
