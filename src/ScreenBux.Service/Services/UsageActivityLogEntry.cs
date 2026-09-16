namespace ScreenBux.Service.Services;

/// <summary>
/// One logged foreground segment - the device was on <see cref="CategoryName"/>
/// (process <see cref="ProcessName"/>, window title <see cref="WindowTitle"/>) continuously
/// from <see cref="StartedAt"/> to <see cref="EndedAt"/>. Written by
/// <see cref="UsageActivityLogWriter"/> as one JSON line per segment.
/// </summary>
public class UsageActivityLogEntry
{
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public long DurationSeconds { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
}
