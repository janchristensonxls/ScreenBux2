namespace ScreenBux.Shared.Models;

/// <summary>
/// Represents a policy for controlling application access
/// </summary>
public class AppPolicy
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public PolicyAction Action { get; set; }
    public List<TimeWindow> AllowedTimeWindows { get; set; } = new();
    public int MaxUsageMinutesPerDay { get; set; }
    public bool BlockOnWeekdays { get; set; }
    public bool BlockOnWeekends { get; set; }
}

public enum PolicyAction
{
    Allow,
    Block,
    TimeRestricted
}

public class TimeWindow
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
}
