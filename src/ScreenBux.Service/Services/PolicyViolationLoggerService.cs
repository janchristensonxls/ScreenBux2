namespace ScreenBux.Service.Services;

/// <summary>
/// Subscribes to <see cref="ProcessKillerService.ProcessEnforcementAttempted"/> and logs every
/// enforcement attempt (real or dry-run). Kept separate from <see cref="ProcessKillerService"/>
/// itself so future subscribers (e.g. a SignalR broadcast to the WebClient for live dry-run
/// visibility) can be added without touching enforcement logic.
/// </summary>
public class PolicyViolationLoggerService : IHostedService
{
    private readonly ILogger<PolicyViolationLoggerService> _logger;
    private readonly ProcessKillerService _processKiller;

    public PolicyViolationLoggerService(ILogger<PolicyViolationLoggerService> logger, ProcessKillerService processKiller)
    {
        _logger = logger;
        _processKiller = processKiller;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processKiller.ProcessEnforcementAttempted += OnProcessEnforcementAttempted;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _processKiller.ProcessEnforcementAttempted -= OnProcessEnforcementAttempted;
        return Task.CompletedTask;
    }

    private void OnProcessEnforcementAttempted(object? sender, ProcessEnforcementEventArgs e)
    {
        var prefix = e.DryRun ? "[DRY-RUN] " : string.Empty;
        _logger.LogInformation(
            "{Prefix}Enforcement {Action} on {ProcessName} (PID: {ProcessId}) - reason: {Reason}",
            prefix, e.Action, e.ProcessName, e.ProcessId, e.Reason ?? "(none)");
    }
}
