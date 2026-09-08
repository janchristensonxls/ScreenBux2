using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Accumulates elapsed usage time locally (via a monotonic timer, immune to wall-clock
/// manipulation mid-session - see docs/decisions/screen-time-usage-tracking.md) and flushes it
/// to the WebServer as a delta on a slower, independent cadence than enforcement polling.
/// Caches the server's last-known cross-device total so <see cref="IsBudgetExceeded"/> is a
/// cheap in-memory check for <see cref="ProcessMonitoringService"/>.
///
/// v1 limitation: all accumulated time is currently attributed to the "Other" category
/// (approximating "device is on"), since foreground-process category matching
/// (<c>AppCategory</c>/<c>AppCategoryRule</c>) is not yet wired through from the Agent's
/// foreground-window reports. The schema and sync plumbing already support per-category
/// breakdowns; only the attribution step is a follow-up.
/// </summary>
public class UsageTrackingService : BackgroundService
{
    private const string DefaultCategoryName = "Other";
    private const int BaselineSyncIntervalSeconds = 300;
    private const int NearLimitSyncIntervalSeconds = 30;
    private const int NearLimitThresholdSeconds = 600;

    private readonly ILogger<UsageTrackingService> _logger;
    private readonly IConfiguration _configuration;
    private readonly DeviceIdentityService _deviceIdentity;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PolicyService _policyService;
    private readonly GrantService _grantService;

    private readonly object _lock = new();
    private long _pendingSeconds;
    private long _totalSecondsToday;
    private DateOnly _cachedEffectiveDate;

    public UsageTrackingService(
        ILogger<UsageTrackingService> logger,
        IConfiguration configuration,
        DeviceIdentityService deviceIdentity,
        IHttpClientFactory httpClientFactory,
        PolicyService policyService,
        GrantService grantService)
    {
        _logger = logger;
        _configuration = configuration;
        _deviceIdentity = deviceIdentity;
        _httpClientFactory = httpClientFactory;
        _policyService = policyService;
        _grantService = grantService;
    }

    /// <summary>Cross-device total for today, per the last successful sync (or 0 if never synced).</summary>
    public long TotalSecondsToday
    {
        get { lock (_lock) { return _totalSecondsToday; } }
    }

    /// <summary>True once the cross-device total for today reaches the child's configured daily budget.</summary>
    public bool IsBudgetExceeded
    {
        get
        {
            var budgetMinutes = _policyService.GetConfiguration().DailyBudgetMinutes;
            if (budgetMinutes is not int minutes)
            {
                return false;
            }

            return TotalSecondsToday >= minutes * 60L;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverBaseUrl = _configuration["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            _logger.LogInformation("ServerBaseUrl not configured; usage tracking disabled.");
            return;
        }

        _cachedEffectiveDate = ComputeEffectiveDate(DateTime.Now, _policyService.GetConfiguration().DayStartHour);

        var stopwatch = Stopwatch.StartNew();
        var lastFlush = stopwatch.Elapsed;
        var lastFlushSuccessful = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            if (!_grantService.IsGrantActive)
            {
                // Approximates "device is on and in use"; see class summary for the v1
                // category-attribution limitation.
                lock (_lock)
                {
                    _pendingSeconds += 1;
                }
            }

            var elapsedSinceFlush = stopwatch.Elapsed - lastFlush;
            var syncIntervalSeconds = BaselineSyncIntervalSeconds;

            // Sync more frequently once the child is close to a configured daily budget, so
            // other devices see a fresher cross-device total right when it matters.
            var budgetMinutes = _policyService.GetConfiguration().DailyBudgetMinutes;
            if (budgetMinutes is int minutes)
            {
                var remainingSeconds = (minutes * 60L) - TotalSecondsToday;
                if (remainingSeconds >= 0 && remainingSeconds <= NearLimitThresholdSeconds)
                {
                    syncIntervalSeconds = NearLimitSyncIntervalSeconds;
                }
            }

            if (elapsedSinceFlush.TotalSeconds >= syncIntervalSeconds || (!lastFlushSuccessful && elapsedSinceFlush.TotalSeconds >= 5))
            {
                lastFlushSuccessful = await FlushAsync(serverBaseUrl, stoppingToken);
                lastFlush = stopwatch.Elapsed;
            }
        }

        // Best-effort final flush so nothing accumulated since the last periodic sync is lost.
        await FlushAsync(serverBaseUrl, CancellationToken.None);
    }

    private async Task<bool> FlushAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var state = _deviceIdentity.GetOrCreate();
        if (!state.IsLinked)
        {
            return true;
        }

        long secondsToFlush;
        lock (_lock)
        {
            secondsToFlush = _pendingSeconds;
        }

        var effectiveDate = ComputeEffectiveDate(DateTime.Now, _policyService.GetConfiguration().DayStartHour);

        if (secondsToFlush <= 0 && effectiveDate == _cachedEffectiveDate)
        {
            return true;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", state.DeviceToken);

            var request = new AddUsageSecondsRequest
            {
                DeviceId = state.DeviceId,
                EffectiveDate = effectiveDate,
                CategoryName = DefaultCategoryName,
                Seconds = secondsToFlush
            };

            using var response = await client.PostAsJsonAsync("api/usage/add", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Usage sync failed ({Status}); will retry.", (int)response.StatusCode);
                return false;
            }

            var summary = await response.Content.ReadFromJsonAsync<UsageSummaryDto>(cancellationToken);

            lock (_lock)
            {
                _pendingSeconds -= secondsToFlush;
                _totalSecondsToday = effectiveDate == _cachedEffectiveDate ? summary?.TotalSeconds ?? _totalSecondsToday : summary?.TotalSeconds ?? 0;
            }

            if (effectiveDate != _cachedEffectiveDate)
            {
                _cachedEffectiveDate = effectiveDate;
            }

            _logger.LogDebug("Synced {Seconds}s of usage for {EffectiveDate}; cross-device total now {TotalSeconds}s.", secondsToFlush, effectiveDate, summary?.TotalSeconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing usage.");
            return false;
        }
    }

    /// <summary>
    /// Computes the device-local "effective day" for a given local time, tilted by
    /// <paramref name="dayStartHour"/> (0 = ordinary midnight). See
    /// docs/decisions/screen-time-usage-tracking.md.
    /// </summary>
    public static DateOnly ComputeEffectiveDate(DateTime localNow, int dayStartHour)
    {
        var date = DateOnly.FromDateTime(localNow);
        if (dayStartHour > 0 && localNow.TimeOfDay < TimeSpan.FromHours(dayStartHour))
        {
            date = date.AddDays(-1);
        }

        return date;
    }
}
