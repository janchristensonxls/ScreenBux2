namespace ScreenBux.Shared.Models;

/// <summary>
/// One logged foreground segment - the device was on <see cref="CategoryName"/>
/// (process <see cref="ProcessName"/>, window title <see cref="WindowTitle"/>) continuously
/// from <see cref="StartedAt"/> to <see cref="EndedAt"/>. Written locally by the Service's
/// <c>UsageActivityLogWriter</c> as one JSON line per segment, and reused as-is when a day's
/// entries are fetched on demand for the WebClient's Logs page (see <see cref="UsageDayLog"/>).
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

/// <summary>
/// One effective day's worth of logged foreground segments, as read from the device's local
/// day-log file. Empty <see cref="Entries"/> means the device had no logged activity that day
/// (or the file has already been pruned - see <c>UsageActivityLogWriter</c>'s retention window).
/// </summary>
public class UsageDayLog
{
    public DateOnly EffectiveDate { get; set; }
    public List<UsageActivityLogEntry> Entries { get; set; } = new();
}

/// <summary>
/// Correlated response to an on-demand "fetch usage logs" request for a specific device and
/// date range, relayed from the Service back through the WebServer hub/REST endpoint to the
/// requesting WebClient. Mirrors <see cref="ProcessListResult"/>/the screen-capture upload shape.
/// </summary>
public class UsageLogsResult
{
    /// <summary>Matches the RequestId supplied by the WebClient when requesting the logs.</summary>
    public Guid RequestId { get; set; }

    public Guid DeviceId { get; set; }

    public bool Success { get; set; } = true;

    public string? ErrorMessage { get; set; }

    /// <summary>Only days with at least one logged segment are included.</summary>
    public List<UsageDayLog> Days { get; set; } = new();
}
