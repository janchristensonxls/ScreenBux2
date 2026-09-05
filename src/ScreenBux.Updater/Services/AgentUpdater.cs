using System.Diagnostics;
using System.IO.Compression;

namespace ScreenBux.Updater.Services;

/// <summary>
/// Stops the running Agent process, replaces its installed files with the contents of a
/// downloaded update package, and relaunches it in the active console session via
/// <see cref="SessionLauncher"/>. The Agent itself can't do any of this to its own running
/// executable, so - like <see cref="ServiceUpdater"/> for the Service - the Updater drives it
/// from a separate process.
/// </summary>
public class AgentUpdater
{
    private const string ProcessName = "ScreenBux.Agent";
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<AgentUpdater> _logger;
    private readonly SessionLauncher _sessionLauncher;

    public AgentUpdater(ILogger<AgentUpdater> logger, SessionLauncher sessionLauncher)
    {
        _logger = logger;
        _sessionLauncher = sessionLauncher;
    }

    /// <summary>
    /// Applies <paramref name="updatePackagePath"/> (a downloaded zip of the published Agent
    /// output) to <paramref name="installDirectory"/>, closing any running Agent process
    /// first and relaunching <paramref name="agentExecutablePath"/> in the active session
    /// afterward. Returns true if the update files were applied successfully; the relaunch
    /// itself is best-effort (see <see cref="SessionLauncher"/> for why it may legitimately
    /// fail, e.g. no interactive session yet).
    /// </summary>
    public bool ApplyUpdate(string updatePackagePath, string installDirectory, string agentExecutablePath)
    {
        try
        {
            StopRunningAgent();

            _logger.LogInformation("Extracting update package {Package} to {InstallDirectory}.", updatePackagePath, installDirectory);
            ZipFile.ExtractToDirectory(updatePackagePath, installDirectory, overwriteFiles: true);

            if (!_sessionLauncher.TryStartInActiveSession(agentExecutablePath))
            {
                _logger.LogInformation("Agent update applied but relaunch was skipped/failed; it will start on next login or the next update check.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Agent update.");
            return false;
        }
    }

    private void StopRunningAgent()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            return;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    _logger.LogInformation("Requesting graceful close of {ProcessName} (PID {Pid}).", ProcessName, process.Id);
                    if (process.CloseMainWindow())
                    {
                        process.WaitForExit((int)GracefulExitTimeout.TotalMilliseconds);
                    }

                    if (!process.HasExited)
                    {
                        _logger.LogWarning("{ProcessName} (PID {Pid}) did not exit gracefully; killing it.", ProcessName, process.Id);
                        process.Kill();
                        process.WaitForExit((int)GracefulExitTimeout.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping {ProcessName} (PID {Pid}).", ProcessName, process.Id);
                }
            }
        }
    }
}
