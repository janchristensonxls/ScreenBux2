namespace ScreenBux.Shared.Models;

/// <summary>
/// A recurring, device-local schedule window (e.g. "Fri-Sun 18:00-22:00", or an overnight
/// "22:00-06:00" bedtime range). Used by <see cref="CategoryPolicy.AllowedWindows"/>,
/// <see cref="SessionRule.Schedule"/> and <see cref="ModeScheduleEntry.Window"/> (selecting the
/// active mode by schedule, see docs/decisions/mode-scheduling-and-temporary-overrides.md).
/// </summary>
public class TimeWindow
{
    /// <summary>Days this window applies to. Empty/null means "every day".</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// True if <paramref name="localNow"/> falls within this window. Handles the case where
    /// <see cref="EndTime"/> is earlier than <see cref="StartTime"/> (e.g. 22:00-06:00) as
    /// wrapping past midnight, rather than treating it as an always-false/empty range.
    /// </summary>
    public bool IsActiveAt(DateTime localNow)
    {
        if (DaysOfWeek.Count > 0)
        {
            var today = localNow.DayOfWeek;
            var yesterday = (DayOfWeek)(((int)today + 6) % 7);

            var wraps = EndTime < StartTime;
            var relevantDay = wraps && localNow.TimeOfDay < EndTime ? yesterday : today;

            if (!DaysOfWeek.Contains(relevantDay))
            {
                return false;
            }
        }

        var time = localNow.TimeOfDay;

        if (EndTime < StartTime)
        {
            // Overnight window: active from StartTime through midnight, then midnight through EndTime.
            return time >= StartTime || time < EndTime;
        }

        return time >= StartTime && time < EndTime;
    }
}
