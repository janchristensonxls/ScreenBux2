namespace ScreenBux.Data.Entities;

/// <summary>
/// Accumulated usage-seconds for one child, on one device, in one app category, for one
/// "effective day" (device-local calendar day, tilted by the child's <c>DayStartHour</c> - see
/// <c>ChildProfile.DayStartHour</c> and <c>docs/decisions/screen-time-usage-tracking.md</c>).
/// One row per (EffectiveDate, ChildProfileId, DeviceId, AppCategoryId) combination, created
/// lazily and updated via delta increments (never one row per usage tick). Per-day/per-child,
/// per-day/per-device, and per-category totals are all just different groupings/filters over
/// this same table.
/// </summary>
public class UsageDailyTotal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The device-local "effective day" this total belongs to (date component only, no time).
    /// </summary>
    public DateOnly EffectiveDate { get; set; }

    public Guid ChildProfileId { get; set; }

    public ChildProfile? ChildProfile { get; set; }

    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    public Guid AppCategoryId { get; set; }

    public AppCategory? AppCategory { get; set; }

    /// <summary>
    /// Accumulated usage in seconds (not minutes) to avoid rounding loss across many small
    /// periodic flushes from the Service.
    /// </summary>
    public long Seconds { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
