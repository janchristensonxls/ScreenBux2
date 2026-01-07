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
}
