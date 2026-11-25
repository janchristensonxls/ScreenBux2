using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenBux.Service.Services;
using ScreenBux.Shared.Messages;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Tests for <see cref="PolicyService"/> running on a <see cref="DevicePolicyBundle"/>: the mode
/// in effect is re-resolved locally as time passes (schedule transitions, override expiry),
/// changes are announced, and the bundle survives a restart via its cache file.
/// </summary>
public class PolicyServiceBundleTests : IDisposable
{
    private static readonly Guid Normal = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid School = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Open = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ScreenBux.Tests.{Guid.NewGuid()}");
    private readonly FakeTime _time = new(new DateTime(2024, 1, 2, 7, 30, 0, DateTimeKind.Utc)); // Tuesday
    private readonly NotificationQueueService _notifications = new();

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private string PolicyPath => Path.Combine(_directory, "policy.json");

    private string BundlePath => Path.Combine(_directory, "policy-bundle.json");

    private PolicyService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyFilePath"] = PolicyPath })
            .Build();
        return new PolicyService(NullLogger<PolicyService>.Instance, configuration, _notifications, _time);
    }

    private static DevicePolicyBundle Bundle(ModeOverride? ov = null) => new()
    {
        Selection = new ModeSelection
        {
            DefaultProfileId = Normal,
            Schedule =
            {
                new ModeScheduleEntry
                {
                    ProfileId = School,
                    Window = new TimeWindow { StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(15) }
                }
            },
            Override = ov
        },
        Profiles =
        {
            [Normal] = new PolicyConfiguration { Name = "Normal" },
            [School] = new PolicyConfiguration { Name = "School", CheckIntervalSeconds = 7 },
            [Open] = new PolicyConfiguration { Name = "Open" }
        }
    };

    private List<PendingNotification> DrainNotifications()
    {
        var list = new List<PendingNotification>();
        while (_notifications.TryDequeue(out var n) && n is not null)
        {
            list.Add(n);
        }

        return list;
    }

    [Fact]
    public async Task UpdateBundle_UsesModeInEffectNow_WithoutAnnouncingIt()
    {
        var service = CreateService();

        await service.UpdateBundleAsync(Bundle());

        Assert.True(service.HasBundle);
        Assert.Equal("Normal", service.GetConfiguration().Name);
        Assert.Equal(ModeSource.Default, service.ActiveMode!.Source);
        Assert.Empty(DrainNotifications());
    }

    [Fact]
    public async Task RefreshActiveMode_SwitchesAtScheduleBoundary_AndAnnouncesIt()
    {
        var service = CreateService();
        await service.UpdateBundleAsync(Bundle());
        ModeChangedEventArgs? raised = null;
        service.ModeChanged += (_, e) => raised = e;

        _time.Now = new DateTime(2024, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        service.RefreshActiveMode();

        Assert.Equal("School", service.GetConfiguration().Name);
        Assert.Equal(7, service.GetConfiguration().CheckIntervalSeconds);
        Assert.Equal("School", raised?.ModeName);
        var notification = Assert.Single(DrainNotifications());
        Assert.Equal(NotificationSeverity.Info, notification.Severity);
        Assert.Contains("School mode is now active until 15:00", notification.Message);

        // Same mode on the next tick: no repeat announcement.
        _time.Now = _time.Now.AddMinutes(1);
        service.RefreshActiveMode();
        Assert.Empty(DrainNotifications());
    }

    [Fact]
    public async Task OverrideExpiresLocally_WithoutServerContact()
    {
        var start = new DateTime(2024, 1, 2, 16, 0, 0, DateTimeKind.Utc);
        _time.Now = start;
        var service = CreateService();
        await service.UpdateBundleAsync(Bundle(new ModeOverride { ProfileId = Open, StartedAtUtc = start, ExpiresAtUtc = start.AddHours(1) }));
        Assert.Equal("Open", service.GetConfiguration().Name);

        _time.Now = start.AddHours(1);
        await service.ReloadPolicyIfChangedAsync(); // the per-tick entry point

        Assert.Equal("Normal", service.GetConfiguration().Name);
    }

    [Fact]
    public async Task Bundle_SurvivesRestart()
    {
        _time.Now = new DateTime(2024, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        await CreateService().UpdateBundleAsync(Bundle());
        Assert.True(File.Exists(BundlePath));

        var restarted = CreateService();
        await restarted.LoadPolicyAsync();

        Assert.True(restarted.HasBundle);
        Assert.Equal("School", restarted.GetConfiguration().Name);
    }

    [Fact]
    public async Task UpdatePolicy_DropsBundle()
    {
        var service = CreateService();
        await service.UpdateBundleAsync(Bundle());

        await service.UpdatePolicyAsync(new PolicyConfiguration { Name = "Legacy" });

        Assert.False(service.HasBundle);
        Assert.Null(service.ActiveMode);
        Assert.False(File.Exists(BundlePath));
        Assert.Equal("Legacy", service.GetConfiguration().Name);

        // Ticks don't bring the bundle back.
        await service.ReloadPolicyIfChangedAsync();
        Assert.Equal("Legacy", service.GetConfiguration().Name);
    }

    [Fact]
    public async Task OlderBundle_IsIgnored()
    {
        var service = CreateService();
        var newer = Bundle(new ModeOverride { ProfileId = Open, StartedAtUtc = _time.Now });
        newer.GeneratedAtUtc = _time.Now;
        var older = Bundle();
        older.GeneratedAtUtc = _time.Now.AddSeconds(-5);

        Assert.True(await service.UpdateBundleAsync(newer));
        Assert.False(await service.UpdateBundleAsync(older));

        Assert.Equal("Open", service.GetConfiguration().Name);
        var restarted = CreateService();
        await restarted.LoadPolicyAsync();
        Assert.Equal("Open", restarted.GetConfiguration().Name);
    }

    [Fact]
    public async Task ConcurrentUpdates_LeaveMemoryAndCacheFileOnNewestBundle()
    {
        var service = CreateService();
        var start = _time.Now;
        var bundles = Enumerable.Range(0, 40)
            .Select(i =>
            {
                var b = Bundle();
                b.GeneratedAtUtc = start.AddSeconds(i);
                b.Profiles[Normal].CheckIntervalSeconds = i + 1; // identifies the bundle
                return b;
            })
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        await Task.WhenAll(bundles.Select(b => Task.Run(() => service.UpdateBundleAsync(b))));

        Assert.Equal(40, service.GetConfiguration().CheckIntervalSeconds);
        var restarted = CreateService();
        await restarted.LoadPolicyAsync();
        Assert.Equal(40, restarted.GetConfiguration().CheckIntervalSeconds);
        Assert.False(File.Exists(BundlePath + ".tmp"));
    }

    /// <summary>Controllable clock; schedules are evaluated in UTC so tests are machine-independent.</summary>
    private sealed class FakeTime(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
