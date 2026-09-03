using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenBux.Service.Services;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Tests for <see cref="ProcessKillerService"/>'s dry-run mode ("Enforcement:DryRun").
/// Dry-run must never actually touch the target process, but must still raise
/// <see cref="ProcessKillerService.ProcessEnforcementAttempted"/> so callers can observe what
/// *would* have happened - this is what lets policy/rule matching be tested "softly".
/// </summary>
public class ProcessKillerServiceTests
{
    private static ProcessKillerService CreateService(bool dryRun)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Enforcement:DryRun"] = dryRun.ToString()
            })
            .Build();

        return new ProcessKillerService(NullLogger<ProcessKillerService>.Instance, configuration);
    }

    /// <summary>
    /// Starts a disposable, windowless child process that sleeps long enough for the test to
    /// interact with it. Caller is responsible for ensuring it is not left running (tests kill
    /// it themselves in dry-run cases; the real enforcement path kills it in live cases).
    /// </summary>
    private static Process StartLongRunningTestProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 60 /nobreak >nul",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process!;
    }

    [Fact]
    public async Task TryCloseProcessAsync_WhenDryRun_DoesNotTouchProcess_ButRaisesEvent()
    {
        var service = CreateService(dryRun: true);
        var process = StartLongRunningTestProcess();

        ProcessEnforcementEventArgs? capturedEvent = null;
        service.ProcessEnforcementAttempted += (_, e) => capturedEvent = e;

        try
        {
            var result = await service.TryCloseProcessAsync(process.Id, "Test Rule");

            Assert.True(result);
            process.Refresh();
            Assert.False(process.HasExited);

            Assert.NotNull(capturedEvent);
            Assert.True(capturedEvent!.DryRun);
            Assert.Equal(process.Id, capturedEvent.ProcessId);
            Assert.Equal("Test Rule", capturedEvent.Reason);
            Assert.Equal("Close", capturedEvent.Action);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            process.Dispose();
        }
    }

    [Fact]
    public async Task KillProcessTreeAsync_WhenDryRun_DoesNotTouchProcess_ButRaisesEvent()
    {
        var service = CreateService(dryRun: true);
        var process = StartLongRunningTestProcess();

        ProcessEnforcementEventArgs? capturedEvent = null;
        service.ProcessEnforcementAttempted += (_, e) => capturedEvent = e;

        try
        {
            var result = await service.KillProcessTreeAsync(process.Id, "Test Rule");

            Assert.True(result);
            process.Refresh();
            Assert.False(process.HasExited);

            Assert.NotNull(capturedEvent);
            Assert.True(capturedEvent!.DryRun);
            Assert.Equal("KillTree", capturedEvent.Action);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            process.Dispose();
        }
    }

    [Fact]
    public async Task TryCloseProcessAsync_WhenNotDryRun_ActuallyClosesProcess_AndRaisesEvent()
    {
        var service = CreateService(dryRun: false);
        var process = StartLongRunningTestProcess();

        ProcessEnforcementEventArgs? capturedEvent = null;
        service.ProcessEnforcementAttempted += (_, e) => capturedEvent = e;

        try
        {
            var result = await service.TryCloseProcessAsync(process.Id, "Test Rule");

            Assert.True(result);
            process.Refresh();
            Assert.True(process.HasExited);

            Assert.NotNull(capturedEvent);
            Assert.False(capturedEvent!.DryRun);
            Assert.Equal("Test Rule", capturedEvent.Reason);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            process.Dispose();
        }
    }

    [Fact]
    public async Task KillProcessTreeAsync_WhenNotDryRun_ActuallyKillsProcess()
    {
        var service = CreateService(dryRun: false);
        var process = StartLongRunningTestProcess();

        try
        {
            var result = await service.KillProcessTreeAsync(process.Id, "Test Rule");

            Assert.True(result);
            process.Refresh();
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            process.Dispose();
        }
    }
}
