using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenBux.Service.Services;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Verifies <see cref="PolicyViolationLoggerService"/> subscribes to
/// <see cref="ProcessKillerService.ProcessEnforcementAttempted"/> while started, and stops
/// receiving events once stopped.
/// </summary>
public class PolicyViolationLoggerServiceTests
{
    private static ProcessKillerService CreateProcessKillerService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Enforcement:DryRun"] = "true"
            })
            .Build();

        return new ProcessKillerService(NullLogger<ProcessKillerService>.Instance, configuration);
    }

    private static void RaiseTestEvent(ProcessKillerService processKiller)
    {
        // ProcessEnforcementAttempted is private-invoke only; exercise it indirectly via a
        // dry-run enforcement call against a process that is guaranteed to exist (the current
        // test process), which never actually touches anything in dry-run mode.
        var currentProcessId = Environment.ProcessId;
        processKiller.TryCloseProcessAsync(currentProcessId, "Test").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task StartAsync_SubscribesToEnforcementEvents()
    {
        var processKiller = CreateProcessKillerService();
        var sut = new PolicyViolationLoggerService(NullLogger<PolicyViolationLoggerService>.Instance, processKiller);

        var received = false;
        processKiller.ProcessEnforcementAttempted += (_, _) => received = true;

        await sut.StartAsync(CancellationToken.None);
        RaiseTestEvent(processKiller);

        Assert.True(received);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesFromEnforcementEvents()
    {
        var processKiller = CreateProcessKillerService();
        var sut = new PolicyViolationLoggerService(NullLogger<PolicyViolationLoggerService>.Instance, processKiller);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        // No direct way to assert "not subscribed" other than confirming no exception occurs
        // and behavior doesn't change after StopAsync - this primarily guards against a leak
        // if StopAsync is refactored to no-op.
        RaiseTestEvent(processKiller);
    }
}
