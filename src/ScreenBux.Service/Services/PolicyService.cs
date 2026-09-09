using System.Text.Json;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for loading and managing policies from JSON configuration
/// </summary>
public class PolicyService
{
    private readonly ILogger<PolicyService> _logger;
    private PolicyConfiguration _configuration;
    private readonly string _policyFilePath;
    private DateTime? _lastWriteTimeUtc;

    /// <summary>
    /// True once the Service has successfully synced policy from the server at least once
    /// since process startup (via either the REST poll or the SignalR push). Used to gate
    /// device-wide power actions (Sleep/Hibernate) so a stale cached policy.json left over
    /// from before a reboot can never fire a lockout action before the real current mode is
    /// confirmed from the server.
    /// </summary>
    public bool HasSyncedSinceStartup { get; private set; }

    public void MarkSyncedSinceStartup() => HasSyncedSinceStartup = true;

    public PolicyService(ILogger<PolicyService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _policyFilePath = configuration["PolicyFilePath"] ?? PolicyStorage.GetDefaultPolicyPath();
        PolicyStorage.EnsurePolicyDirectory(_policyFilePath);
        _configuration = new PolicyConfiguration();
    }

    public async Task LoadPolicyAsync()
    {
        try
        {
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

    public async Task ReloadPolicyIfChangedAsync()
    {
        try
        {
            if (!File.Exists(_policyFilePath))
            {
                return;
            }

            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(_policyFilePath);
            if (_lastWriteTimeUtc == null || lastWriteTimeUtc > _lastWriteTimeUtc)
            {
                await LoadPolicyAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking policy file for changes");
        }
    }

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

    public PolicyConfiguration GetConfiguration() => _configuration;

    public async Task UpdatePolicyAsync(PolicyConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _configuration = configuration;
        await SavePolicyAsync();
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
