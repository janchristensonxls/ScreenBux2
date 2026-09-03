using System.Diagnostics;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for monitoring background (non-interactive-focused) processes and enforcing
/// policy rules against process-name-only matches (i.e. rules with no WindowTitleRegex,
/// or the ProcessNameRegex branch of rules that also have one).
///
/// Foreground/window-title enforcement is NOT done here. A real Windows Service runs in
/// Session 0 on a non-interactive window station and cannot see the interactive user's
/// desktop/windows (GetForegroundWindow, EnumWindows, etc. are window-station scoped), so
/// title-based rules can never be evaluated correctly from this process. That
/// responsibility belongs to ScreenBux.Agent, which runs inside the user's session, detects
/// the real foreground window/title, and reports it to this Service over the Named Pipe
/// (see NamedPipeServerService.HandleProcessReportAsync). This loop only catches
/// process-name matches for processes the Agent hasn't (yet) reported as foreground.
/// </summary>
public class ProcessMonitoringService : BackgroundService
{
    private readonly ILogger<ProcessMonitoringService> _logger;
    private readonly PolicyService _policyService;
    private readonly ProcessKillerService _processKiller;
    private readonly PolicySyncService _policySync;

    public ProcessMonitoringService(
        ILogger<ProcessMonitoringService> logger,
        PolicyService policyService,
        ProcessKillerService processKiller,
        PolicySyncService policySync)
    {
        _logger = logger;
        _policyService = policyService;
        _processKiller = processKiller;
        _policySync = policySync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Process monitoring service started at: {time}", DateTimeOffset.Now);

        await _policyService.LoadPolicyAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await _policyService.ReloadPolicyIfChangedAsync();

            var config = _policyService.GetConfiguration();
            if (config.EnableMonitoring)
            {
                await EnforcePoliciesAsync(config, stoppingToken);
            }

            var delaySeconds = Math.Max(1, config.CheckIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _logger.LogInformation("Process monitoring service stopping at: {time}", DateTimeOffset.Now);
    }

    private async Task EnforcePoliciesAsync(PolicyConfiguration config, CancellationToken stoppingToken)
    {
        var handledProcesses = new HashSet<int>();

        // The executable path is only consulted by the legacy AppPolicy matching path,
        // which is dead whenever regex Rules exist. Resolving it requires Process.MainModule,
        // which throws Win32Exception for every protected/system process we can't open -
        // producing a flood of first-chance exceptions. Only resolve it when it can matter.
        var resolveExecutablePath = !config.Rules.Any(r => r.Enabled) && config.Policies.Count > 0;

        foreach (var process in Process.GetProcesses())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var processInfo = CreateProcessInfo(process, resolveExecutablePath);
            if (processInfo == null)
            {
                continue;
            }

            // isForegroundWindow: false - this loop has no reliable window-title data for
            // any of these processes, so only ProcessNameRegex/name-based matching applies.
            var rule = _policyService.GetMatchingRule(processInfo, isForegroundWindow: false);
            if (rule != null)
            {
                if (handledProcesses.Add(processInfo.ProcessId))
                {
                    await CloseProcessAsync(processInfo, rule.Name);
                }

                continue;
            }

            if (_policyService.ShouldBlockProcess(processInfo, isForegroundWindow: false) && handledProcesses.Add(processInfo.ProcessId))
            {
                await CloseProcessAsync(processInfo, "Legacy policy");
            }
        }
    }

    private async Task CloseProcessAsync(ProcessInfo processInfo, string ruleName)
    {
        _logger.LogWarning(
            "Process {ProcessName} (PID: {ProcessId}) matched rule {RuleName}, attempting closure",
            processInfo.ProcessName,
            processInfo.ProcessId,
            ruleName);

        await _processKiller.TryCloseProcessAsync(processInfo.ProcessId, ruleName);

        processInfo.DetectedAt = DateTime.UtcNow;
        await _policySync.SendProcessDetectionAsync(processInfo);
    }

    private ProcessInfo? CreateProcessInfo(Process process, bool resolveExecutablePath)
    {
        try
        {
            return new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                ExecutablePath = resolveExecutablePath ? GetProcessExecutablePath(process) : string.Empty,
                DetectedAt = DateTime.UtcNow
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string GetProcessExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
