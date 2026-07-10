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
