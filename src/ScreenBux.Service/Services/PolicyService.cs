using System.Text.Json;
using System.Text.RegularExpressions;
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
                _configuration = JsonSerializer.Deserialize<PolicyConfiguration>(json) ?? new PolicyConfiguration();
                _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_policyFilePath);
                _logger.LogInformation("Policy loaded successfully with {Count} policies", _configuration.Policies.Count);
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
            var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions
            {
                WriteIndented = true
            });
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
            Rules = new List<PolicyRule>
            {
                new PolicyRule
                {
                    Name = "Example Blocked App",
                    ProcessNameRegex = "^example$",
                    WindowTitleRegex = string.Empty,
                    Enabled = true
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

    public bool ShouldBlockProcess(ProcessInfo processInfo, bool isForegroundWindow = true)
    {
        if (!_configuration.EnableMonitoring)
            return false;

        if (_configuration.Rules.Count > 0)
        {
            return GetMatchingRule(processInfo, isForegroundWindow) != null;
        }

        var policy = _configuration.Policies.FirstOrDefault(p =>
            processInfo.ProcessName.Contains(p.ApplicationName, StringComparison.OrdinalIgnoreCase) ||
            processInfo.ExecutablePath.Contains(p.ExecutablePath, StringComparison.OrdinalIgnoreCase));

        if (policy == null)
            return false;

        return policy.Action switch
        {
            PolicyAction.Block => true,
            PolicyAction.TimeRestricted => !IsWithinAllowedTime(policy),
            _ => false
        };
    }

    public PolicyRule? GetMatchingRule(ProcessInfo processInfo, bool isForegroundWindow)
    {
        foreach (var rule in _configuration.Rules.Where(rule => rule.Enabled))
        {
            if (IsRegexMatch(rule.ProcessNameRegex, processInfo.ProcessName))
            {
                return rule;
            }

            if (isForegroundWindow && IsRegexMatch(rule.WindowTitleRegex, processInfo.WindowTitle))
            {
                return rule;
            }
        }

        return null;
    }

    private bool IsRegexMatch(string? pattern, string input)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(input ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex pattern in policy: {Pattern}", pattern);
            return false;
        }
    }

    private bool IsWithinAllowedTime(AppPolicy policy)
    {
        var now = DateTime.Now;
        var currentTime = TimeOnly.FromDateTime(now);
        var currentDay = now.DayOfWeek;

        // Check day-based blocks
        if (policy.BlockOnWeekdays && currentDay >= DayOfWeek.Monday && currentDay <= DayOfWeek.Friday)
            return false;
        if (policy.BlockOnWeekends && (currentDay == DayOfWeek.Saturday || currentDay == DayOfWeek.Sunday))
            return false;

        // Check time windows
        if (policy.AllowedTimeWindows.Count == 0)
            return false;

        return policy.AllowedTimeWindows.Any(window =>
            window.DaysOfWeek.Contains(currentDay) &&
            currentTime >= window.StartTime &&
            currentTime <= window.EndTime);
    }
}
