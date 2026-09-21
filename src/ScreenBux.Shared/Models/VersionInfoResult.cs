namespace ScreenBux.Shared.Models;

/// <summary>
/// Result of an on-demand "get version" request for a device, reported back to the
/// WebServer's hub by the Service (see MonitoringHub.RequestVersionInfo/ReportVersionInfo).
/// Reports both the auto-updater's "installed version" marker file (what ScreenBux.Updater
/// actually compares against the WebServer's manifest) and the currently running process's
/// assembly version (which can lag behind the marker, or vice versa, if an update hasn't been
/// applied yet or the process hasn't been restarted since) - for both the Service and the Agent,
/// the latter obtained by round-tripping through the Service via the named pipe (see
/// VersionInfoReportMessage).
/// </summary>
public class VersionInfoResult
{
    public Guid RequestId { get; set; }
    public Guid DeviceId { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? ServiceInstalledVersion { get; set; }
    public string? ServiceRunningVersion { get; set; }
    public string? AgentInstalledVersion { get; set; }
    public string? AgentRunningVersion { get; set; }
}
