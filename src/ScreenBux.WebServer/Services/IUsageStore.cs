using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Persistence for accumulated per-day usage totals, keyed by child/device/category. WebServer
/// is the sole authoritative writer; the Service submits delta increments (never absolute
/// values), and the Service/WebClient only read/observe (via REST and the "UsageUpdated"
/// SignalR event). See docs/decisions/screen-time-usage-tracking.md.
/// </summary>
public interface IUsageStore
{
    /// <summary>
    /// Adds (never overwrites) elapsed usage seconds for one device/category/effective-day,
    /// upserting the underlying <c>UsageDailyTotal</c> row. Unrecognized/omitted category names
    /// resolve to the account's "Other" category, created lazily on first use.
    /// </summary>
    Task<UsageSummaryDto> AddUsageSecondsAsync(string accountId, AddUsageSecondsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a child's usage summary (cross-device total, per-category and per-device
    /// breakdowns) for one effective day.
    /// </summary>
    Task<UsageSummaryDto> GetSummaryAsync(string accountId, Guid childProfileId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
