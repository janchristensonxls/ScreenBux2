namespace ScreenBux.Shared.Models;

/// <summary>
/// One category's accumulated usage for one "effective day" (device-local, tilted by
/// <c>ChildProfile.DayStartHour</c>) - used across REST responses and the SignalR
/// "UsageUpdated" broadcast.
/// </summary>
public class UsageCategoryTotalDto
{
    public Guid AppCategoryId { get; set; }

    public string AppCategoryName { get; set; } = string.Empty;

    public long Seconds { get; set; }
}

/// <summary>
/// A child's usage totals for one effective day, both the cross-device total and the
/// per-device/per-category breakdown it's built from. One shape, reused for "per day/per child"
/// and "per day/per device" views - the latter is just this same data filtered to one device.
/// </summary>
public class UsageSummaryDto
{
    public Guid ChildProfileId { get; set; }

    public DateOnly EffectiveDate { get; set; }

    /// <summary>Total seconds across all of the child's devices and categories.</summary>
    public long TotalSeconds { get; set; }

    /// <summary>Breakdown by category, summed across all of the child's devices.</summary>
    public List<UsageCategoryTotalDto> ByCategory { get; set; } = new();

    /// <summary>Breakdown by device, summed across all categories.</summary>
    public List<UsageDeviceTotalDto> ByDevice { get; set; } = new();
}

/// <summary>One device's total seconds for the effective day, across all categories.</summary>
public class UsageDeviceTotalDto
{
    public Guid DeviceId { get; set; }

    public long Seconds { get; set; }
}

/// <summary>
/// Request from the Service to add elapsed usage seconds for one device/category, for a given
/// effective day. A delta increment, not an absolute value - mirrors
/// <c>AddGrantMinutesRequest</c>'s add/subtract shape, applied via an upsert on the server.
/// </summary>
public class AddUsageSecondsRequest
{
    public Guid DeviceId { get; set; }

    public DateOnly EffectiveDate { get; set; }

    /// <summary>
    /// Category name to attribute this usage to. Resolved/created server-side against the
    /// account's <c>AppCategory</c> rows; unrecognized/omitted names fall back to "Other".
    /// </summary>
    public string? CategoryName { get; set; }

    public long Seconds { get; set; }
}
