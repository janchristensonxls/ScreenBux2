using System.Text.Json;
using ScreenBux.Shared.Messages;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Services;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Holds the policy the Service enforces. Normally fed a <see cref="DevicePolicyBundle"/> from
/// the server (cached in policy-bundle.json): the bundle carries every mode this device may use
/// plus the default/schedule/override selection, and the mode in effect is re-resolved with
/// <see cref="ModeResolver"/> on every tick (<see cref="RefreshActiveMode"/>), so schedule
/// transitions and override expiry happen locally - also while offline. Falls back to a single
/// <see cref="PolicyConfiguration"/> (policy.json) when no bundle has been received, e.g. from a
/// server that predates bundles. See docs/decisions/mode-scheduling-and-temporary-overrides.md.
/// </summary>
public class PolicyService
{
    private readonly ILogger<PolicyService> _logger;
    private readonly NotificationQueueService? _notificationQueue;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    // Serializes everything that reads or writes the cache files (bundle updates from the REST
    // poll and the SignalR push, tick reloads, legacy policy updates), so memory and
    // policy-bundle.json can't end up holding different bundles.
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private PolicyConfiguration _configuration;
    private DevicePolicyBundle? _bundle;
    private ModeResolution? _activeMode;
    private readonly string _policyFilePath;
    private readonly string _bundleFilePath;
    private readonly TimeSpan _syncStalenessThreshold;
    private DateTime? _lastWriteTimeUtc;
    private DateTime? _lastBundleWriteTimeUtc;
    private DateTime? _lastSyncedUtc;

    /// <summary>
    /// True while the Service has successfully synced policy from the server "recently enough"
    /// (via either the REST poll or the SignalR push) - see <see cref="_syncStalenessThreshold"/>.
    /// Used to gate device-wide power actions (Sleep/Hibernate) so a stale cached policy.json
    /// can never fire a lockout action before the real current mode is reconfirmed from the
    /// server. This is deliberately staleness-based (re-evaluated on every read) rather than a
    /// one-shot "synced since process startup" latch: the process does NOT restart after a
    /// Sleep/Hibernate power action (only the OS suspends), so a one-shot flag would stay stuck
    /// "true" forever after the very first sync and would never re-gate a lockout rule that
    /// fires again immediately after the device wakes back up, before the next poll/push has
    /// had a chance to run.
    /// </summary>
    public bool HasSyncedSinceStartup =>
        _lastSyncedUtc.HasValue && DateTime.UtcNow - _lastSyncedUtc.Value <= _syncStalenessThreshold;

    public void MarkSyncedSinceStartup() => _lastSyncedUtc = DateTime.UtcNow;

    /// <summary>True when the policy comes from a <see cref="DevicePolicyBundle"/> (not a single legacy policy).</summary>
    public bool HasBundle => _bundle is not null;

    /// <summary>Which mode is in effect, why, and until when; null without a bundle.</summary>
    public ModeResolution? ActiveMode => _activeMode;

    /// <summary>Raised when the mode in effect changes (not on the first resolution after startup).</summary>
    public event EventHandler<ModeChangedEventArgs>? ModeChanged;

    public PolicyService(
        ILogger<PolicyService> logger,
        IConfiguration configuration,
        NotificationQueueService? notificationQueue = null,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _notificationQueue = notificationQueue;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _policyFilePath = configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
        _bundleFilePath = configuration["PolicyBundleFilePath"]
            ?? Path.Combine(Path.GetDirectoryName(_policyFilePath) ?? AppContext.BaseDirectory, "policy-bundle.json");
        var stalenessSeconds = int.TryParse(configuration["PolicySyncStalenessSeconds"], out var s) ? s : 180;
        _syncStalenessThreshold = TimeSpan.FromSeconds(Math.Max(1, stalenessSeconds));
        PolicyStorage.EnsurePolicyDirectory(_policyFilePath);
        _configuration = new PolicyConfiguration();
    }

    public async Task LoadPolicyAsync()
    {
        await _fileGate.WaitAsync();
        try
        {
            await LoadPolicyCoreAsync();
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>Loads the cache files. Callers must hold <see cref="_fileGate"/>.</summary>
    private async Task LoadPolicyCoreAsync()
    {
        try
        {
            if (File.Exists(_bundleFilePath))
            {
                var json = await File.ReadAllTextAsync(_bundleFilePath);
                var bundle = JsonSerializer.Deserialize<DevicePolicyBundle>(json, PolicyJsonOptions.Default);
                if (bundle is not null)
                {
                    _lastBundleWriteTimeUtc = File.GetLastWriteTimeUtc(_bundleFilePath);
                    Apply(bundle);
                    _logger.LogInformation("Policy bundle loaded with {Count} modes; active mode {Mode}.",
                        bundle.Profiles.Count, _configuration.Name);
                    return;
                }
            }

            lock (_lock)
            {
                _bundle = null;
                _activeMode = null;
            }

            if (File.Exists(_policyFilePath))
            {
                var json = await File.ReadAllTextAsync(_policyFilePath);
                _configuration = JsonSerializer.Deserialize<PolicyConfiguration>(json, PolicyJsonOptions.Default) ?? new PolicyConfiguration();
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_policyFilePath);
                _logger.LogInformation("Policy loaded successfully with {Count} categories, {RuleCount} category policies",
                    _configuration.AppCategories.Count, _configuration.CategoryPolicies.Count);
            }
            else
            {
                _logger.LogWarning("Policy file not found at {Path}, using default configuration", _policyFilePath);
                await CreateDefaultPolicyAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading policy file");
            _configuration = new PolicyConfiguration();
        }
    }

    /// <summary>
    /// Called every enforcement tick: reloads the cache files if something else rewrote them,
    /// then re-resolves the mode in effect (schedule transitions, override expiry).
    /// </summary>
    public async Task ReloadPolicyIfChangedAsync()
    {
        await _fileGate.WaitAsync();
        try
        {
            if (File.Exists(_bundleFilePath))
            {
                var bundleWriteTimeUtc = File.GetLastWriteTimeUtc(_bundleFilePath);
                if (_lastBundleWriteTimeUtc == null || bundleWriteTimeUtc > _lastBundleWriteTimeUtc)
                {
                    await LoadPolicyCoreAsync();
                }
            }
            else if (File.Exists(_policyFilePath))
            {
                var lastWriteTimeUtc = File.GetLastWriteTimeUtc(_policyFilePath);
                if (_lastWriteTimeUtc == null || lastWriteTimeUtc > _lastWriteTimeUtc)
                {
                    await LoadPolicyCoreAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking policy file for changes");
        }
        finally
        {
            _fileGate.Release();
        }

        RefreshActiveMode();
    }

    /// <summary>
    /// Re-resolves the mode in effect from the bundle at the current time and switches
    /// <see cref="GetConfiguration"/> to it. No-op without a bundle.
    /// </summary>
    public void RefreshActiveMode() => Apply(incoming: null);

    public async Task SavePolicyAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_configuration, PolicyJsonOptions.Default);
            await File.WriteAllTextAsync(_policyFilePath, json);
            _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_policyFilePath);
            _logger.LogInformation("Policy saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving policy file");
        }
    }

    private async Task CreateDefaultPolicyAsync()
    {
        _configuration = new PolicyConfiguration
        {
            EnableMonitoring = true,
            CheckIntervalSeconds = 5,
            LogActivity = true,
            AppCategories = new List<AppCategoryConfig>
            {
                new AppCategoryConfig
                {
                    Name = "Example Blocked App",
                    Rules = new List<AppCategoryRuleConfig>
                    {
                        new AppCategoryRuleConfig
                        {
                            ProcessNameRegex = "^example$",
                            WindowTitleRegex = string.Empty,
                            Enabled = true
                        }
                    }
                }
            },
            CategoryPolicies = new List<CategoryPolicy>
            {
                new CategoryPolicy
                {
                    CategoryNames = new List<string> { "Example Blocked App" },
                    Enforcement = CategoryPolicyEnforcement.Blocked
                }
            }
        };
        await SavePolicyAsync();
    }


    /// <summary>The policy in effect right now (the resolved mode's policy when a bundle is in use).</summary>
    public PolicyConfiguration GetConfiguration() => _configuration;

    /// <summary>
    /// Replaces the policy with a single fixed <paramref name="configuration"/> and drops any
    /// bundle - the path for servers that don't serve bundles yet.
    /// </summary>
    public async Task UpdatePolicyAsync(PolicyConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        await _fileGate.WaitAsync();
        try
        {
            lock (_lock)
            {
                _bundle = null;
                _activeMode = null;
                _configuration = configuration;
            }

            TryDeleteBundleFile();
            await SavePolicyAsync();
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// Switches to <paramref name="bundle"/> (unless it is older than the current one - see
    /// <see cref="DevicePolicyBundle.GeneratedAtUtc"/>) and caches it to disk. The whole
    /// apply + write runs under <see cref="_fileGate"/>, and the file is replaced atomically, so
    /// concurrent updates (REST poll and SignalR push) leave memory and file on the same bundle.
    /// Returns false if the bundle was ignored as stale.
    /// </summary>
    public async Task<bool> UpdateBundleAsync(DevicePolicyBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        await _fileGate.WaitAsync();
        try
        {
            if (!Apply(bundle))
            {
                return false;
            }

            try
            {
                PolicyStorage.EnsurePolicyDirectory(_bundleFilePath);
                var tempPath = _bundleFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(bundle, PolicyJsonOptions.Default));
                File.Move(tempPath, _bundleFilePath, overwrite: true);
                _lastBundleWriteTimeUtc = File.GetLastWriteTimeUtc(_bundleFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving policy bundle file");
            }

            return true;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// Resolves <paramref name="incoming"/> (or, when null, the current bundle again) at the
    /// current time and makes it the active policy. Read, resolve and swap happen under one lock,
    /// so a tick's refresh can't put back a bundle that a concurrent update just replaced.
    /// Returns false when there is nothing to apply or <paramref name="incoming"/> is stale.
    /// </summary>
    private bool Apply(DevicePolicyBundle? incoming)
    {
        ModeResolution? previous;
        PolicyConfiguration policy;
        ModeResolution resolution;

        lock (_lock)
        {
            var bundle = incoming ?? _bundle;
            if (bundle is null)
            {
                return false;
            }

            if (incoming is not null
                && incoming.GeneratedAtUtc is DateTime incomingAt
                && _bundle?.GeneratedAtUtc is DateTime currentAt
                && incomingAt < currentAt)
            {
                _logger.LogInformation("Ignoring policy bundle generated at {Incoming:o}; already on one from {Current:o}.", incomingAt, currentAt);
                return false;
            }

            (policy, resolution) = ModeResolver.ResolvePolicy(bundle, _timeProvider.GetUtcNow().UtcDateTime, _timeProvider.LocalTimeZone);
            previous = _activeMode;
            _bundle = bundle;
            _configuration = policy;
            _activeMode = resolution;
        }

        if (previous is not null && previous.ProfileId != resolution.ProfileId)
        {
            OnModeChanged(policy.Name, resolution);
        }

        return true;
    }

    private void OnModeChanged(string modeName, ModeResolution resolution)
    {
        _logger.LogInformation("Mode changed to {Mode} ({Source}) until {UntilUtc}.", modeName, resolution.Source, resolution.UntilUtc);

        var until = FormatUntil(resolution.UntilUtc);
        _notificationQueue?.Enqueue(new PendingNotification
        {
            Severity = NotificationSeverity.Info,
            Title = "Mode changed",
            Message = string.IsNullOrEmpty(until) ? $"{modeName} mode is now active." : $"{modeName} mode is now active {until}.",
            AutoDismissSeconds = 10
        });

        ModeChanged?.Invoke(this, new ModeChangedEventArgs(modeName, resolution));
    }

    /// <summary>"until 15:00", "until Mon 07:00", or empty when there is no known end.</summary>
    private string FormatUntil(DateTime? untilUtc)
    {
        if (untilUtc is not DateTime utc)
        {
            return string.Empty;
        }

        var timeZone = _timeProvider.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, timeZone);

        return local.Date == nowLocal.Date
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"until {local:HH:mm}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"until {local:ddd HH:mm}");
    }

    private void TryDeleteBundleFile()
    {
        try
        {
            if (File.Exists(_bundleFilePath))
            {
                File.Delete(_bundleFilePath);
            }

            _lastBundleWriteTimeUtc = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting policy bundle file");
        }
    }


    /// <summary>
    /// Classifies a process/window into an <see cref="AppCategoryConfig"/> by matching its
    /// regex rules in order; returns null if no category matches (implicit "Other").
    /// </summary>
    public AppCategoryConfig? ClassifyProcess(ProcessInfo processInfo, bool isForegroundWindow)
    {
        foreach (var category in _configuration.AppCategories)
        {
            foreach (var rule in category.Rules)
            {
                if (rule.Matches(processInfo.ProcessName, processInfo.WindowTitle, isForegroundWindow))
                {
                    return category;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the effective <see cref="CategoryPolicy"/> for a given category name (or the
    /// implicit "Other" bucket when <paramref name="categoryName"/> is null), defaulting to
    /// <see cref="CategoryPolicyEnforcement.Allowed"/> when no explicit policy is configured.
    /// A <see cref="CategoryPolicy"/> may govern multiple category names (see
    /// <see cref="CategoryPolicy.CategoryNames"/>); the first policy (in list order) whose
    /// <see cref="CategoryPolicy.CategoryNames"/> contains <paramref name="categoryName"/> wins.
    /// </summary>
    public CategoryPolicy GetCategoryPolicy(string? categoryName)
    {
        var policy = categoryName != null
            ? _configuration.CategoryPolicies.FirstOrDefault(p => p.CategoryNames.Contains(categoryName))
            : null;

        return policy ?? new CategoryPolicy { CategoryNames = new List<string> { categoryName ?? "Other" }, Enforcement = CategoryPolicyEnforcement.Allowed };
    }

    /// <summary>
    /// True if the given process should be blocked right now, per its category's resolved
    /// <see cref="CategoryPolicy"/>. <paramref name="usedSecondsToday"/> is only consulted for
    /// <see cref="CategoryPolicyEnforcement.TimeLimited"/> categories, and should already be the
    /// sum of usage across every category name in the resolved policy's
    /// <see cref="CategoryPolicy.CategoryNames"/> (see
    /// <c>UsageTrackingService.GetTotalSecondsTodayForCategories</c>).
    /// </summary>
    public bool ShouldBlockProcess(ProcessInfo processInfo, bool isForegroundWindow, long usedSecondsToday = 0)
    {
        if (!_configuration.EnableMonitoring)
        {
            return false;
        }

        var category = ClassifyProcess(processInfo, isForegroundWindow);
        var policy = GetCategoryPolicy(category?.Name);

        return IsBlockedByPolicy(policy, usedSecondsToday);
    }

    /// <summary>
    /// True if <paramref name="policy"/> currently blocks its category, accounting for
    /// <see cref="CategoryPolicy.AllowedWindows"/> and, for <see cref="CategoryPolicyEnforcement.TimeLimited"/>,
    /// <paramref name="usedSecondsToday"/> against <see cref="CategoryPolicy.DailyBudgetMinutes"/>.
    /// </summary>
    public static bool IsBlockedByPolicy(CategoryPolicy policy, long usedSecondsToday)
    {
        if (policy.Enforcement == CategoryPolicyEnforcement.Allowed)
        {
            return false;
        }

        if (policy.AllowedWindows.Count > 0 && !policy.AllowedWindows.Any(w => w.IsActiveAt(DateTime.Now)))
        {
            return true;
        }

        if (policy.Enforcement == CategoryPolicyEnforcement.Blocked)
        {
            return true;
        }

        // TimeLimited
        return policy.DailyBudgetMinutes is int minutes && usedSecondsToday >= minutes * 60L;
    }

    /// <summary>
    /// Returns enabled session rules that are currently active per their schedule (or always,
    /// if unscheduled), e.g. a "Sleep" lockout mode. Callers should evaluate these once per
    /// policy tick.
    /// </summary>
    public IReadOnlyList<SessionRule> GetActiveSessionRules() =>
        _configuration.SessionRules.Where(rule => rule.IsActiveAt(DateTime.Now)).ToList();
}

/// <summary>Raised by <see cref="PolicyService.ModeChanged"/>.</summary>
public sealed class ModeChangedEventArgs(string modeName, ModeResolution resolution) : EventArgs
{
    public string ModeName { get; } = modeName;

    public ModeResolution Resolution { get; } = resolution;
}
