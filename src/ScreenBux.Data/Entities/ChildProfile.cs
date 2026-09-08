namespace ScreenBux.Data.Entities;

/// <summary>
/// A person (child) whose time budget can span multiple devices.
/// </summary>
public class ChildProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Hour (0-23, device-local time) at which this child's "day" resets for usage-tracking
    /// purposes. 0 = ordinary midnight. E.g. 5 = a "5am-to-5am" day, so a late night doesn't
    /// reset the daily budget right at midnight. See
    /// docs/decisions/screen-time-usage-tracking.md.
    /// </summary>
    public int DayStartHour { get; set; }

    /// <summary>
    /// Total accumulated-usage-per-day budget across all of this child's devices, in minutes.
    /// Null means no total-day budget is enforced (per-category budgets, if any, still apply).
    /// </summary>
    public int? DailyBudgetMinutes { get; set; }

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
