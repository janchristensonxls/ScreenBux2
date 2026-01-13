using System.Diagnostics;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for monitoring processes and enforcing policy rules.
/// </summary>
public class ProcessMonitoringService : BackgroundService
{
    private readonly ILogger<ProcessMonitoringService> _logger;
    private readonly PolicyService _policyService;
    private readonly ProcessKillerService _processKiller;
    private readonly ForegroundWindowDetector _foregroundWindowDetector;

    public ProcessMonitoringService(
        ILogger<ProcessMonitoringService> logger,
        PolicyService policyService,
        ProcessKillerService processKiller)
    {
        _logger = logger;
        _policyService = policyService;
        _processKiller = processKiller;
        _foregroundWindowDetector = new ForegroundWindowDetector();
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
                await EnforcePoliciesAsync(stoppingToken);
            }

            var delaySeconds = Math.Max(1, config.CheckIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _logger.LogInformation("Process monitoring service stopping at: {time}", DateTimeOffset.Now);
    }

    private async Task EnforcePoliciesAsync(CancellationToken stoppingToken)
    {
        var handledProcesses = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var processInfo = CreateProcessInfo(process);
            if (processInfo == null)
            {
                continue;
            }

            var rule = _policyService.GetMatchingRule(processInfo, false);
            if (rule != null)
            {
                if (handledProcesses.Add(processInfo.ProcessId))
                {
                    await CloseProcessAsync(processInfo, rule.Name);
                }

                continue;
            }

            if (_policyService.ShouldBlockProcess(processInfo, false) && handledProcesses.Add(processInfo.ProcessId))
            {
                await CloseProcessAsync(processInfo, "Legacy policy");
            }
        }

        var foregroundProcess = _foregroundWindowDetector.GetForegroundProcessInfo();
        if (foregroundProcess != null)
        {
            var rule = _policyService.GetMatchingRule(foregroundProcess, true);
            if (rule != null && handledProcesses.Add(foregroundProcess.ProcessId))
            {
                await CloseProcessAsync(foregroundProcess, rule.Name);
                return;
            }

            if (_policyService.ShouldBlockProcess(foregroundProcess, true) && handledProcesses.Add(foregroundProcess.ProcessId))
            {
                await CloseProcessAsync(foregroundProcess, "Legacy policy");
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

        await _processKiller.TryCloseProcessAsync(processInfo.ProcessId);
    }

    private ProcessInfo? CreateProcessInfo(Process process)
    {
        try
        {
            return new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                ExecutablePath = GetProcessExecutablePath(process),
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
