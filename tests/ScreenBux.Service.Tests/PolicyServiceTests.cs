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
    public async Task GetActiveSessionRules_ReturnsOnlyEnabledActiveSessionRules()
    {
        var config = new PolicyConfiguration
        {
            SessionRules = new List<SessionRule>
            {
                new() { Name = "DisabledLockout", Enabled = false },
                new() { Name = "Lockout", Action = PolicyRuleAction.Sleep, Enabled = true }
            }
        };
        var service = await CreateServiceWithConfigurationAsync(config);

        var activeRules = service.GetActiveSessionRules();

        var rule = Assert.Single(activeRules);
        Assert.Equal("Lockout", rule.Name);
        Assert.Equal(PolicyRuleAction.Sleep, rule.Action);
    }

    [Fact]
    public async Task GetActiveSessionRules_ReturnsEmpty_WhenNoSessionRulesConfigured()
    {
        var config = new PolicyConfiguration();
        var service = await CreateServiceWithConfigurationAsync(config);

        Assert.Empty(service.GetActiveSessionRules());
    }

    [Fact]
    public void SessionRule_DefaultsToEnabledAndSleep()
    {
        var rule = new SessionRule();

        Assert.True(rule.Enabled);
        Assert.Equal(PolicyRuleAction.Sleep, rule.Action);
    }
}
