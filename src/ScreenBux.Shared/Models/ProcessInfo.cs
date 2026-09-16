namespace ScreenBux.Shared.Models;

/// <summary>
/// Represents information about a running process
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// How long the interactive session has had no keyboard/mouse input, as measured by the
    /// Agent via GetLastInputInfo at <see cref="DetectedAt"/>. Lets a category opt into pausing
    /// usage accumulation while idle (see <see cref="CategoryPolicy.IdleTimeOutMode"/>) without
    /// treating idle input as "stepped away" for every category (e.g. watching a video).
    /// </summary>
    public TimeSpan IdleTime { get; set; }

    /// <summary>
    /// Identifies the device this process was detected on. Empty until the device is linked.
    /// </summary>
    public Guid? DeviceId { get; set; }
}
