using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly GrantService _grantService;
    private readonly UsageTrackingService _usageTracking;
    private readonly ProcessKillerService _processKiller;
    private readonly PolicySyncService _policySync;
    private readonly PowerActionService _powerAction;

    /// <summary>
    /// Minimum time to wait between forced Sleep/Hibernate power actions. Without this, a
    /// wake-armed input device (keyboard/mouse) can resume the machine almost immediately
    /// after SetSuspendState suspends it; on the very next monitoring tick the same
    /// session-rule/budget condition is still true and the loop would suspend again right
    /// away, producing a rapid sleep/wake oscillation instead of staying asleep.
    /// </summary>
    private static readonly TimeSpan PowerActionCooldown = TimeSpan.FromMinutes(2);

    private DateTime? _lastPowerActionUtc;

    public ProcessMonitoringService(
        ILogger<ProcessMonitoringService> logger,
        PolicyService policyService,
        GrantService grantService,
        UsageTrackingService usageTracking,
        ProcessKillerService processKiller,
        PolicySyncService policySync,
        PowerActionService powerAction)
    {
        _logger = logger;
        _policyService = policyService;
        _grantService = grantService;
        _usageTracking = usageTracking;
        _processKiller = processKiller;
        _policySync = policySync;
        _powerAction = powerAction;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Process monitoring service started at: {time}", DateTimeOffset.Now);

        await _policyService.LoadPolicyAsync();
        await _grantService.LoadGrantAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Fallback delay if the tick fails before the policy-configured interval can be
            // read - an unforeseen failure here (e.g. a malformed regex in a synced category
            // rule) must never escape this loop and take the whole Service down; see the catch
            // below.
            var delaySeconds = 5;

            try
            {
                await _policyService.ReloadPolicyIfChangedAsync();

                var config = _policyService.GetConfiguration();
                delaySeconds = Math.Max(1, config.CheckIntervalSeconds);

                if (config.EnableMonitoring)
                {
                    if (_grantService.IsGrantActive)
                    {
                        _logger.LogDebug("Skipping enforcement; a time grant is active until {ExpiresAtUtc}.", _grantService.ExpiresAtUtc);
                    }
                    else if (IsPowerActionOnCooldown())
                    {
                        _logger.LogDebug("Skipping power-action enforcement; still within cooldown after the last Sleep/Hibernate.");
                    }
                    else if (_usageTracking.IsBudgetExceeded)
                    {
                        // An independent, third enforcement check - deliberately not folded into
                        // PolicyRule/AppPolicy matching so it can't be silently shadowed by
                        // whichever rule system currently wins there. See
                        // docs/decisions/screen-time-usage-tracking.md.
                        _logger.LogWarning(
                            "Daily usage budget exceeded ({TotalSeconds}s used); triggering {Action} action.",
                            _usageTracking.TotalSecondsToday, config.DailyBudgetExceededAction);
                        ExecutePowerAction(config.DailyBudgetExceededAction);
                        _lastPowerActionUtc = DateTime.UtcNow;
                    }
                    else if (!EnforceAlwaysRules())
                    {
                        await EnforcePoliciesAsync(config, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service is shutting down - let the loop condition below end it normally.
            }
            catch (Exception ex)
            {
                // A single bad tick (e.g. a malformed regex in a synced policy) must never stop
                // enforcement entirely - log it and try again next tick instead.
                _logger.LogError(ex, "Error during process monitoring enforcement tick; will retry next tick.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _logger.LogInformation("Process monitoring service stopping at: {time}", DateTimeOffset.Now);
    }

    /// <summary>
    /// Evaluates active <see cref="SessionRule"/>s (e.g. a "Sleep" lockout mode), which apply to
    /// the whole session rather than a specific process. Gated on <see cref="PolicyService.HasSyncedSinceStartup"/>
    /// so a stale cached policy.json left over from before a reboot (or from before the device
    /// went to sleep) can never trigger a power action before the real current mode has been
    /// recently reconfirmed with the server.
    /// Returns true if a power action was executed (short-circuiting further per-process
    /// enforcement for this tick, since the device is about to suspend).
    /// </summary>
    private bool IsPowerActionOnCooldown()
    {
        return _lastPowerActionUtc is { } last && DateTime.UtcNow - last < PowerActionCooldown;
    }

    private bool EnforceAlwaysRules()
    {
        var activeRules = _policyService.GetActiveSessionRules();
        if (activeRules.Count == 0)
        {
            return false;
        }

        if (!_policyService.HasSyncedSinceStartup)
        {
            _logger.LogDebug("Skipping session rules until policy has synced with the server since startup.");
            return false;
        }

        var rule = activeRules.FirstOrDefault(r => r.Action is PolicyRuleAction.Sleep or PolicyRuleAction.Hibernate or PolicyRuleAction.PowerOff);
        if (rule is null)
        {
            return false;
        }

        _logger.LogWarning("Session rule {RuleName} triggered a device-wide {Action} action", rule.Name, rule.Action);

        ExecutePowerAction(rule.Action);
        _lastPowerActionUtc = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Dispatches to the appropriate <see cref="PowerActionService"/> method for the given
    /// action. Any value other than Sleep/Hibernate/PowerOff (e.g. a mismatched
    /// CloseProcess/KillProcessTree left in <see cref="PolicyConfiguration.DailyBudgetExceededAction"/>
    /// by mistake) falls back to Sleep as the safe default.
    /// </summary>
    private void ExecutePowerAction(PolicyRuleAction action)
    {
        switch (action)
        {
            case PolicyRuleAction.Hibernate:
                _powerAction.Hibernate();
                break;
            case PolicyRuleAction.PowerOff:
                _powerAction.PowerOff();
                break;
            default:
                _powerAction.Sleep();
                break;
        }
    }

    private async Task EnforcePoliciesAsync(PolicyConfiguration config, CancellationToken stoppingToken)
    {
        var handledProcesses = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var processInfo = CreateProcessInfo(process, resolveExecutablePath: false);
            if (processInfo == null)
            {
                continue;
            }

            // isForegroundWindow: false - this loop has no reliable window-title data for
            // any of these processes, so only ProcessNameRegex/name-based matching applies.
            var category = _policyService.ClassifyProcess(processInfo, isForegroundWindow: false);
            var categoryPolicy = _policyService.GetCategoryPolicy(category?.Name);

            if (PolicyService.IsBlockedByPolicy(categoryPolicy, _usageTracking.GetTotalSecondsTodayForCategories(categoryPolicy.CategoryNames)) && handledProcesses.Add(processInfo.ProcessId))
            {
                await EnforceRuleAsync(processInfo, categoryPolicy, category?.Name);
            }
        }
    }

    private async Task EnforceRuleAsync(ProcessInfo processInfo, CategoryPolicy policy, string? categoryName)
    {
        var ruleName = categoryName ?? "Other";

        if (policy.Action == PolicyRuleAction.KillProcessTree)
        {
            _logger.LogWarning(

                processInfo.ProcessName, processInfo.ProcessId, ruleName);

            await _processKiller.KillProcessTreeAsync(processInfo.ProcessId, ruleName);
            processInfo.DetectedAt = DateTime.UtcNow;
            await _policySync.SendProcessDetectionAsync(processInfo);
            return;
        }

        await CloseProcessAsync(processInfo, ruleName);
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

    /// <summary>
    /// Enumerates all currently running processes on this device for an on-demand "get process
    /// list" request. Unlike the enforcement loop's <see cref="EnforcePoliciesAsync"/>, this
    /// always resolves the executable path (best-effort) since it's a one-off, user-triggered
    /// call rather than a per-tick hot path shared by every process on the machine.
    ///
    /// Restricted to the active interactive console session, which drops Session-0
    /// services/system processes that are never meaningful to show a parent - typically
    /// cutting a raw ~500-process enumeration down to a much smaller, relevant set before
    /// any further window-based enrichment/filtering happens upstream.
    /// </summary>
    public IReadOnlyList<ProcessInfo> GetCurrentProcesses()
    {
        var activeSessionId = GetActiveConsoleSessionId();
        var processes = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            if (activeSessionId.HasValue)
            {
                try
                {
                    if (process.SessionId != activeSessionId.Value)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }
            }

            var processInfo = CreateProcessInfo(process, resolveExecutablePath: true);
            if (processInfo != null)
            {
                processes.Add(processInfo);
            }
        }

        return processes;
    }

    /// <summary>
    /// Returns the active console session id, or null if it can't be determined (e.g. no
    /// interactive session logged in) - in which case callers should skip session filtering
    /// rather than exclude everything.
    /// </summary>
    private static uint? GetActiveConsoleSessionId()
    {
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            return sessionId == 0xFFFFFFFF ? null : sessionId;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
