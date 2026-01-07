using System.Text.Json;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for loading and managing policies from JSON configuration
/// </summary>
public class PolicyService
{
    private readonly ILogger<PolicyService> _logger;
    private PolicyConfiguration _configuration;
    private readonly string _policyFilePath;

    public PolicyService(ILogger<PolicyService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _policyFilePath = configuration["PolicyFilePath"] ?? "policy.json";
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

    public async Task SavePolicyAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_policyFilePath, json);
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
            Policies = new List<AppPolicy>
            {
                new AppPolicy
                {
                    ApplicationName = "Example Blocked App",
                    ExecutablePath = "example.exe",
                    Action = PolicyAction.Block,
                    BlockOnWeekdays = true,
                    BlockOnWeekends = true
                }
            }
        };
        await SavePolicyAsync();
    }

    public PolicyConfiguration GetConfiguration() => _configuration;

    public bool ShouldBlockProcess(ProcessInfo processInfo)
    {
        if (!_configuration.EnableMonitoring)
            return false;

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
