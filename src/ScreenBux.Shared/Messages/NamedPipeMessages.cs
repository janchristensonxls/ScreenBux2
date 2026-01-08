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
