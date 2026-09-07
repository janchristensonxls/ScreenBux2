namespace ScreenBux.Shared.Models;

/// <summary>
/// Describes a single visible top-level window, as enumerated by the Agent (the only
/// component that can see the interactive desktop - the Service runs in Session 0 and
/// has no window station access). Used to enrich the Service's process list with real
/// window titles/visibility so on-demand "get process list" requests can be filtered
/// down to windowed, user-facing applications instead of all ~500 OS processes.
/// </summary>
public class WindowInfo
{
    public int ProcessId { get; set; }
    public string WindowTitle { get; set; } = string.Empty;
}
