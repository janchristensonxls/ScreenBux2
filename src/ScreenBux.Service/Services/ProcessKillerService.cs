using System.Diagnostics;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for killing processes and their child processes (kill-tree)
/// </summary>
public class ProcessKillerService
{
    private readonly ILogger<ProcessKillerService> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Raised for every enforcement attempt, whether real or dry-run. Subscribe to this to
    /// observe policy enforcement without necessarily acting on process lifetimes yourself.
    /// </summary>
    public event EventHandler<ProcessEnforcementEventArgs>? ProcessEnforcementAttempted;

    public ProcessKillerService(ILogger<ProcessKillerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// True when enforcement should be observed/logged but must not actually terminate anything -
    /// either locally (this process) or remotely (instructing the Agent to close a process).
    /// </summary>
    public bool IsDryRun => _configuration.GetValue<bool>("Enforcement:DryRun");

    /// <summary>
    /// Kills a process and all its child processes
    /// </summary>
    public async Task<bool> KillProcessTreeAsync(int processId, string? reason = null)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process == null)
            {
                _logger.LogWarning("Process {ProcessId} not found", processId);
                return false;
            }

            if (IsDryRun)
            {
                _logger.LogInformation(
                    "[DRY-RUN] Would kill process tree for PID {ProcessId} ({ProcessName}). Reason: {Reason}",
                    processId, process.ProcessName, reason ?? "(none)");
                RaiseEnforcementEvent(processId, process.ProcessName, reason, "KillTree", dryRun: true);
                return true;
            }

            _logger.LogInformation("Killing process tree for PID {ProcessId} ({ProcessName})", 
                processId, process.ProcessName);
            RaiseEnforcementEvent(processId, process.ProcessName, reason, "KillTree", dryRun: false);

            return await KillSingleProcessAsync(processId, killEntireTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process tree for PID {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Kills a single process. When <paramref name="killEntireTree"/> is true, uses the
    /// built-in Process.Kill(entireProcessTree: true) overload (.NET 5+), which walks the
    /// real parent/child process snapshot on Windows and kills descendants too - this is
    /// what actually makes a "kill tree" call correct, e.g. for multi-process browsers
    /// (Chromium-based apps like Avast Browser spawn renderer/GPU child processes) where
    /// killing only the main window's PID can otherwise leave orphaned children running.
    /// </summary>
    private async Task<bool> KillSingleProcessAsync(int processId, bool killEntireTree = false)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process != null && !process.HasExited)
            {
                process.Kill(killEntireTree);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cts.Token);
                _logger.LogInformation("Process {ProcessId} killed successfully", processId);
                return true;
            }
            return false;
        }
        catch (ArgumentException)
        {
            // Process already exited
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Attempts to gracefully close a process by sending a close message
    /// </summary>
    public async Task<bool> TryCloseProcessAsync(int processId, string? reason = null)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process == null || process.HasExited)
            {
                return true;
            }

            if (IsDryRun)
            {
                _logger.LogInformation(
                    "[DRY-RUN] Would attempt to close process {ProcessName} (PID: {ProcessId}). Reason: {Reason}",
                    process.ProcessName, processId, reason ?? "(none)");
                RaiseEnforcementEvent(processId, process.ProcessName, reason, "Close", dryRun: true);
                return true;
            }

            _logger.LogInformation("Attempting to gracefully close process {ProcessId}", processId);
            RaiseEnforcementEvent(processId, process.ProcessName, reason, "Close", dryRun: false);

            // Try to close the main window gracefully
            if (process.CloseMainWindow())
            {
                // Wait for up to 5 seconds for the process to exit
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    _logger.LogInformation("Process {ProcessId} closed gracefully", processId);
                    return true;
                }
                catch (TaskCanceledException)
                {
                    // Timeout waiting for exit
                }
            }

            // If graceful close failed, kill the process
            _logger.LogWarning("Graceful close failed for process {ProcessId}, forcing kill", processId);
            return await KillProcessTreeAsync(processId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error trying to close process {ProcessId}", processId);
            return false;
        }
    }

    private void RaiseEnforcementEvent(int processId, string processName, string? reason, string action, bool dryRun)
    {
        ProcessEnforcementAttempted?.Invoke(this, new ProcessEnforcementEventArgs
        {
            ProcessId = processId,
            ProcessName = processName,
            Reason = reason,
            Action = action,
            DryRun = dryRun
        });
    }
}
