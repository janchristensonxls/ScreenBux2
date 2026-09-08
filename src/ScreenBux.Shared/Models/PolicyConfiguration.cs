namespace ScreenBux.Shared.Models;

/// <summary>
/// Root configuration for parental control policies
/// </summary>
public class PolicyConfiguration
{
    /// <summary>
    /// Display name of the policy profile this configuration was loaded from (e.g. "Normal",
    /// "School", "Sleep"). Populated by the server when the policy is read/pushed; not itself
    /// the source of truth (that's <c>PolicyProfile.Name</c> in ScreenBux.Data) but carried
    /// along so downstream consumers - like the Service and Agent - can display which policy
    /// is currently applied without a separate round-trip.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    public List<PolicyRule> Rules { get; set; } = new();
    public List<AppPolicy> Policies { get; set; } = new();
    public bool EnableMonitoring { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 5;
    public bool LogActivity { get; set; } = true;

    /// <summary>
    /// Hour (0-23, device-local time) at which the "day" resets for usage-tracking purposes.
    /// 0 = ordinary midnight. Mirrors <c>ChildProfile.DayStartHour</c> - piggybacks on the
    /// existing policy sync pipeline as a stopgap until per-child config has its own sync path.
    /// See docs/decisions/screen-time-usage-tracking.md.
    /// </summary>
    public int DayStartHour { get; set; }

    /// <summary>
    /// Total accumulated-usage-per-day budget across all of this device's child's devices, in
    /// minutes. Null means no total-day budget is enforced. Mirrors
    /// <c>ChildProfile.DailyBudgetMinutes</c>.
    /// </summary>
    public int? DailyBudgetMinutes { get; set; }
}
