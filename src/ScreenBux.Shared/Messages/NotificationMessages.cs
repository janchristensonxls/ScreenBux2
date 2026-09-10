namespace ScreenBux.Shared.Messages;

/// <summary>
/// Severity drives both the overlay's visual style and which audio cue plays. Reserved values
/// beyond the current Warning/TimeUp usage (e.g. Info) are for a future "send message from the
/// WebClient" feature.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Reserved for a future parent-initiated message.</summary>
    Info = 0,

    /// <summary>e.g. "5 minutes left" - non-blocking, auto-dismisses.</summary>
    Warning = 1,

    /// <summary>Hard stop notice - requires explicit dismissal.</summary>
    TimeUp = 2
}

/// <summary>
/// A notification queued for a specific device. Carried piggybacked on
/// <see cref="CommandResponse.PendingNotification"/> until the Agent's next poll picks it up -
/// mirrors the existing PendingWindowListRequestId / PendingScreenCaptureRequestId pattern, so no
/// persistent duplex pipe connection is required.
/// </summary>
public class PendingNotification
{
    public Guid NotificationId { get; set; } = Guid.NewGuid();
    public NotificationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Null means the notification must be explicitly dismissed (used for TimeUp).</summary>
    public int? AutoDismissSeconds { get; set; }

    /// <summary>Category this relates to, if any - lets the WebClient link back to policy.</summary>
    public string? CategoryName { get; set; }
}

/// <summary>
/// Sent from Agent to Service after a notification has been shown/dismissed, so the Service can
/// relay the acknowledgement to the WebServer (which broadcasts it over SignalR to the parent).
/// </summary>
public class NotificationAckMessage : Contracts.INamedPipeMessage
{
    public string MessageType => "NotificationAck";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid NotificationId { get; set; }

    /// <summary>
    /// True if the overlay was shown/dismissed successfully; false if e.g. exclusive fullscreen
    /// blocked it (the audio cue still plays regardless - see ForegroundWindowDetector's
    /// fullscreen heuristic notes).
    /// </summary>
    public bool Shown { get; set; }
    public DateTime AcknowledgedAtUtc { get; set; } = DateTime.UtcNow;
}
