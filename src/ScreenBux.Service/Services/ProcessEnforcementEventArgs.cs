namespace ScreenBux.Service.Services;

/// <summary>
/// Raised whenever <see cref="ProcessKillerService"/> attempts to enforce policy against a
/// process, whether or not the process was actually closed. In dry-run mode no process is
/// actually touched; this event is the only signal that an enforcement action would have
/// happened, which lets us test policy/rule matching "softly" during development.
/// </summary>
public class ProcessEnforcementEventArgs : EventArgs
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    /// <summary>
    /// Why enforcement was attempted (e.g. the matching <c>PolicyRule.Name</c>). May be null
    /// when the caller didn't supply a reason.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The kind of enforcement action attempted: "Close" (graceful) or "KillTree" (forceful).
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// True when this was a simulated action (see appsettings "Enforcement:DryRun") - the
    /// process was NOT actually closed or killed.
    /// </summary>
    public required bool DryRun { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
