using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenBux.Shared.Services;

namespace ScreenBux.Service.Services;

/// <summary>
/// Watches for the Agent process (<c>ScreenBux.Agent.exe</c>) going away while a user is
/// actively logged in, and relaunches it via <see cref="SessionLauncher"/> if so.
///
/// Rationale: the Agent is the Service's only source of foreground-window/title information
/// (see <see cref="NamedPipeServerService.HandleProcessReportAsync"/>), since a real Windows
/// Service runs in Session 0 and can't see the interactive desktop. An ordinary user can simply
/// end the Agent process (Task Manager, taskkill, etc.) to blind window-title enforcement
/// entirely, while process-name-only rules (<see cref="ProcessMonitoringService"/>) keep working.
/// This watchdog closes that gap by periodically checking whether the Agent is running and, if
/// not, relaunching it into the active console session using the same
/// WTSQueryUserToken/CreateProcessAsUser technique the Updater uses when it relaunches the
/// Agent after an update.
///
/// This is a mitigation, not a hard guarantee: a sufficiently determined/technical user could
/// still block relaunch (e.g. by deleting the executable) or race the check interval. It does
/// raise the bar well beyond "close the window once".
/// </summary>
public class AgentWatchdogService : BackgroundService
{
    private const string AgentProcessName = "ScreenBux.Agent";
    private const int DefaultCheckIntervalSeconds = 15;
    private const int MinimumCheckIntervalSeconds = 5;

    private readonly ILogger<AgentWatchdogService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SessionLauncher _sessionLauncher;

    public AgentWatchdogService(
        ILogger<AgentWatchdogService> logger,
        IConfiguration configuration,
        SessionLauncher sessionLauncher)
    {
        _logger = logger;
        _configuration = configuration;
        _sessionLauncher = sessionLauncher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent watchdog started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckAndRelaunchAgentIfNeeded();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking Agent liveness.");
            }

            var intervalSeconds = Math.Max(
                MinimumCheckIntervalSeconds,
                _configuration.GetValue<int?>("AgentWatchdog:CheckIntervalSeconds") ?? DefaultCheckIntervalSeconds);

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Agent watchdog stopping.");
    }

    private void CheckAndRelaunchAgentIfNeeded()
    {
        // The Service typically runs as LocalSystem, which can see Agent processes across every
        // session on the machine (stale/disconnected/other-user sessions), not just the active
        // console session the current user is actually sitting at. A name-only, machine-wide
        // check would treat such a stray instance as "the Agent is running" and silently skip
        // relaunch forever, even though the active session has no Agent at all. Scope the check
        // to the active console session's id to avoid that trap.
        var activeConsoleSessionId = WTSGetActiveConsoleSessionId();
        var runningAgents = Process.GetProcessesByName(AgentProcessName);
        try
        {
            var runningInActiveSession = Array.Exists(runningAgents, p => TryGetSessionId(p) == (int)activeConsoleSessionId);
            if (runningInActiveSession)
            {
                _logger.LogDebug(
                    "Agent process ({ProcessName}) is already running in the active console session {SessionId}.",
                    AgentProcessName, activeConsoleSessionId);
                return;
            }

            if (runningAgents.Length > 0)
            {
                _logger.LogWarning(
                    "Agent process ({ProcessName}) is running but not in the active console session {SessionId} (found in session(s) {FoundSessions}); attempting relaunch anyway.",
                    AgentProcessName, activeConsoleSessionId, string.Join(", ", Array.ConvertAll(runningAgents, TryGetSessionId)));
            }

            var executablePath = _configuration["Agent:ExecutablePath"];
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                _logger.LogDebug(
                    "Agent is not running and no valid Agent:ExecutablePath is configured (got {Path}); skipping relaunch attempt.",
                    executablePath);
                return;
            }

            _logger.LogWarning(
                "Agent process ({ProcessName}) is not running; attempting to relaunch it in the active console session, if any.",
                AgentProcessName);

            // TryStartInActiveSession is itself a no-op (returns false, logs) when there is no
            // active console session, which is exactly the "only if we have a current active
            // interactive session" behavior we want here - no separate session check needed.
            var relaunched = _sessionLauncher.TryStartInActiveSession(executablePath);
            _logger.LogWarning(
                "TryStartInActiveSession({Executable}) returned {Result}.",
                executablePath, relaunched);
        }
        finally
        {
            foreach (var process in runningAgents)
            {
                process.Dispose();
            }
        }
    }

    private static int TryGetSessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch
        {
            // Process may have exited between enumeration and this call; treat as "unknown"
            // rather than throw out of the loop.
            return -1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
