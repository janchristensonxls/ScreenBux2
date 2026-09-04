using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenBux.Service.Services;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Tests for <see cref="PolicyService"/>'s "Always"-condition rule handling and the
/// <see cref="PolicyService.HasSyncedSinceStartup"/> safety gate used before a device-wide
/// power action (Sleep/Hibernate) is allowed to fire.
/// </summary>
public class PolicyServiceTests
{
    private static PolicyService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyFilePath"] = Path.Combine(Path.GetTempPath(), $"ScreenBux.Tests.{Guid.NewGuid()}.policy.json")
            })
            .Build();
        return new PolicyService(NullLogger<PolicyService>.Instance, configuration);
    }

    private static async Task<PolicyService> CreateServiceWithConfigurationAsync(PolicyConfiguration config)
    {
        var service = CreateService();
        await service.UpdatePolicyAsync(config);
        return service;
    }

    [Fact]
    public void HasSyncedSinceStartup_DefaultsToFalse()
    {
        var service = CreateService();

        Assert.False(service.HasSyncedSinceStartup);
    }

    [Fact]
    public void MarkSyncedSinceStartup_SetsFlagToTrue()
    {
        var service = CreateService();

        service.MarkSyncedSinceStartup();

        Assert.True(service.HasSyncedSinceStartup);
    }

    [Fact]
    public async Task GetAlwaysRules_ReturnsOnlyEnabledAlwaysConditionRules()
    {
        var config = new PolicyConfiguration
        {
            Rules = new List<PolicyRule>
            {
                new() { Name = "ProcessRule", ConditionKind = PolicyConditionKind.ProcessMatch, Enabled = true },
                new() { Name = "DisabledAlways", ConditionKind = PolicyConditionKind.Always, Enabled = false },
                new() { Name = "Lockout", ConditionKind = PolicyConditionKind.Always, Action = PolicyRuleAction.Sleep, Enabled = true }
            }
        };
        var service = await CreateServiceWithConfigurationAsync(config);

        var alwaysRules = service.GetAlwaysRules();

        var rule = Assert.Single(alwaysRules);
        Assert.Equal("Lockout", rule.Name);
        Assert.Equal(PolicyRuleAction.Sleep, rule.Action);
    }

    [Fact]
    public async Task GetAlwaysRules_ReturnsEmpty_WhenNoAlwaysRulesConfigured()
    {
        var config = new PolicyConfiguration
        {
            Rules = new List<PolicyRule>
            {
                new() { Name = "ProcessRule", ConditionKind = PolicyConditionKind.ProcessMatch, Enabled = true }
            }
        };
        var service = await CreateServiceWithConfigurationAsync(config);

        Assert.Empty(service.GetAlwaysRules());
    }

    [Fact]
    public void PolicyRule_DefaultsToProcessMatchAndCloseProcess()
    {
        var rule = new PolicyRule();

        Assert.Equal(PolicyConditionKind.ProcessMatch, rule.ConditionKind);
        Assert.Equal(PolicyRuleAction.CloseProcess, rule.Action);
    }
}
