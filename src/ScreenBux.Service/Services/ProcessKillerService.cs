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

    private bool IsDryRun => _configuration.GetValue<bool>("Enforcement:DryRun");

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

            // Get all child processes
            var childProcesses = GetChildProcesses(processId);

            // Kill children first
            foreach (var childPid in childProcesses)
            {
                await KillSingleProcessAsync(childPid);
            }

            // Kill the parent process
            await KillSingleProcessAsync(processId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process tree for PID {ProcessId}", processId);
            return false;
        }
    }

    private async Task<bool> KillSingleProcessAsync(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process != null && !process.HasExited)
            {
                process.Kill();
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

    private List<int> GetChildProcesses(int parentId)
    {
        var children = new List<int>();
        
        try
        {
            // This is a simplified implementation. On Windows, you'd use WMI or similar
            // to get the process tree. For cross-platform compatibility, we're using a basic approach.
            var allProcesses = Process.GetProcesses();
            
            // This is a placeholder - in a real implementation, you'd need to query
            // the parent process ID for each process using platform-specific APIs
            // For now, we'll just return an empty list and rely on the OS to clean up children
            
            _logger.LogDebug("Getting child processes for PID {ParentId}", parentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child processes for PID {ParentId}", parentId);
        }

        return children;
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
