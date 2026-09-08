namespace ScreenBux.Shared.Models;

/// <summary>
/// A recurring, device-local schedule window (e.g. "Fri-Sun 18:00-22:00", or an overnight
/// "22:00-06:00" bedtime range). Reused in two places today - <see cref="CategoryPolicy.AllowedWindows"/>
/// and <see cref="SessionRule.Schedule"/> - and reserved for a possible future third use
/// (auto-selecting the active <c>PolicyProfile</c> by schedule; not implemented yet, see
/// docs/decisions/group-based-policy-model.md).
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
