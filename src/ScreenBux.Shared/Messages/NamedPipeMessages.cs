using ScreenBux.Shared.Models;

namespace ScreenBux.Shared.Messages;

/// <summary>
/// Message sent from Agent to Service to report a detected foreground window
/// </summary>
public class ProcessReportMessage : Contracts.INamedPipeMessage
{
    public string MessageType => "ProcessReport";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ProcessInfo? Process { get; set; }

    /// <summary>
    /// Identifies the reporting device. Empty until the device is linked to an account.
    /// </summary>
    public Guid? DeviceId { get; set; }
}

/// <summary>
/// Command sent from Service to Agent to close a process
/// </summary>
public class CloseProcessCommand : Contracts.INamedPipeMessage
{
    public string MessageType => "CloseProcess";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ProcessId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response sent from Agent back to Service
/// </summary>
public class CommandResponse : Contracts.INamedPipeMessage
{
    public string MessageType => "Response";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Piggybacks a pending on-demand "get window list" request onto this response, since the
    /// Agent already polls the Service every couple of seconds via <see cref="ProcessReportMessage"/>.
    /// When set, the Agent should enumerate its visible windows and reply with a
    /// <see cref="WindowListReportMessage"/> carrying the same RequestId. Avoids needing a
    /// persistent duplex pipe connection for this occasional, user-triggered request.
    /// </summary>
    public Guid? PendingWindowListRequestId { get; set; }

    /// <summary>
    /// Piggybacks a pending on-demand "capture all screens" request onto this response, using
    /// the same polling mechanism as <see cref="PendingWindowListRequestId"/>. When set, the
    /// Agent should capture all monitors and reply with a <see cref="ScreenCaptureReportMessage"/>
    /// carrying the same RequestId.
    /// </summary>
    public Guid? PendingScreenCaptureRequestId { get; set; }

    /// <summary>
    /// Piggybacks a queued notification (e.g. "5 minutes left") onto this response, using the
    /// same polling mechanism as <see cref="PendingWindowListRequestId"/>. When set, the Agent
    /// shows a topmost overlay + audio cue and replies with a <see cref="NotificationAckMessage"/>.
    /// </summary>
    public PendingNotification? PendingNotification { get; set; }
}

/// <summary>
/// Sent from Agent to Service in response to a <see cref="CommandResponse.PendingWindowListRequestId"/>,
/// carrying the currently visible top-level windows so the Service can enrich/filter its
/// on-demand process list down to user-facing applications.
/// </summary>
public class WindowListReportMessage : Contracts.INamedPipeMessage
{
    public string MessageType => "WindowListReport";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid RequestId { get; set; }
    public List<Models.WindowInfo> Windows { get; set; } = new();
}

/// <summary>
/// Sent from Agent to Service in response to a <see cref="CommandResponse.PendingScreenCaptureRequestId"/>,
/// carrying a JPEG-encoded screenshot of each connected monitor so the Service can upload them
/// to the WebServer for on-demand parental viewing.
/// </summary>
public class ScreenCaptureReportMessage : Contracts.INamedPipeMessage
{
    public string MessageType => "ScreenCaptureReport";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<Models.CapturedImage> Images { get; set; } = new();
}

/// <summary>
/// Request to get current policy configuration
/// </summary>
public class GetPolicyRequest : Contracts.INamedPipeMessage
{
    public string MessageType => "GetPolicy";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response with policy configuration
/// </summary>
public class PolicyResponse : Contracts.INamedPipeMessage
{
    public string MessageType => "PolicyResponse";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public PolicyConfiguration? Configuration { get; set; }
}

/// <summary>
/// Sent from Agent to Service to redeem a parent-generated link code and bind this
/// device to an account.
/// </summary>
public class LinkDeviceRequest : Contracts.INamedPipeMessage
{
    public string MessageType => "LinkDevice";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string LinkCode { get; set; } = string.Empty;
}

/// <summary>
/// Response from the Service after a link-code redemption attempt.
/// </summary>
public class LinkDeviceResponse : Contracts.INamedPipeMessage
{
    public string MessageType => "LinkDeviceResponse";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
}

/// <summary>
/// Sent from Agent to Service to ask whether a time grant is currently pausing enforcement,
/// and if so when it expires, so the Agent can render a live countdown.
/// </summary>
public class GrantStatusRequest : Contracts.INamedPipeMessage
{
    public string MessageType => "GrantStatusRequest";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response with the Service's locally cached grant status.
/// </summary>
public class GrantStatusResponse : Contracts.INamedPipeMessage
{
    public string MessageType => "GrantStatusResponse";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
}

