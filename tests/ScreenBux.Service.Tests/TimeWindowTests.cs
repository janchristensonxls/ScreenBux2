using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Tests for <see cref="TimeWindow.IsActiveAt"/>, in particular the overnight-wrap case
/// (e.g. 21:00-06:00 bedtime) shared by <see cref="CategoryPolicy.AllowedWindows"/> and
/// <see cref="SessionRule.Schedule"/>.
/// </summary>
public class TimeWindowTests
{
    private static TimeWindow CreateOvernightWindow(params DayOfWeek[] daysOfWeek) => new()
    {
        DaysOfWeek = daysOfWeek.ToList(),
        StartTime = new TimeSpan(21, 0, 0),
        EndTime = new TimeSpan(6, 0, 0)
    };

    [Fact]
    public void IsActiveAt_OvernightWindow_NoDays_ActiveJustAfterStart()
    {
        var window = CreateOvernightWindow(); // empty DaysOfWeek = every day
        var localNow = new DateTime(2024, 1, 2, 21, 30, 0); // Tuesday 21:30

        Assert.True(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_NoDays_ActiveJustBeforeEnd()
    {
        var window = CreateOvernightWindow();
        var localNow = new DateTime(2024, 1, 3, 5, 59, 0); // Wednesday 05:59

        Assert.True(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_NoDays_InactiveAtExactEnd()
    {
        var window = CreateOvernightWindow();
        var localNow = new DateTime(2024, 1, 3, 6, 0, 0); // Wednesday 06:00 exactly

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_NoDays_InactiveAtExactStartMinusOneMinute()
    {
        var window = CreateOvernightWindow();
        var localNow = new DateTime(2024, 1, 2, 20, 59, 0); // Tuesday 20:59

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_NoDays_InactiveMidday()
    {
        var window = CreateOvernightWindow();
        var localNow = new DateTime(2024, 1, 2, 12, 0, 0); // Tuesday noon

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_ScopedToTuesday_ActiveLateTuesdayNight()
    {
        // Bedtime scoped only to Tuesday nights.
        var window = CreateOvernightWindow(DayOfWeek.Tuesday);
        var localNow = new DateTime(2024, 1, 2, 23, 0, 0); // Tuesday 23:00

        Assert.True(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_ScopedToTuesday_ActiveEarlyWednesdayMorning_AsTailOfTuesdayNight()
    {
        // The 02:00 Wednesday slice is the tail end of Tuesday night's window, so it should
        // still be active even though DaysOfWeek only lists Tuesday (not Wednesday).
        var window = CreateOvernightWindow(DayOfWeek.Tuesday);
        var localNow = new DateTime(2024, 1, 3, 2, 0, 0); // Wednesday 02:00

        Assert.True(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_ScopedToTuesday_InactiveWednesdayNight()
    {
        // Wednesday night (21:00+) should not trigger since only Tuesday is configured -
        // Wednesday's own overnight window (into Thursday) is a separate, unconfigured night.
        var window = CreateOvernightWindow(DayOfWeek.Tuesday);
        var localNow = new DateTime(2024, 1, 3, 22, 0, 0); // Wednesday 22:00

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_ScopedToTuesday_InactiveThursdayMorning()
    {
        // Thursday 02:00 is the tail of Wednesday night, not Tuesday night, so it should be
        // inactive when only Tuesday is configured.
        var window = CreateOvernightWindow(DayOfWeek.Tuesday);
        var localNow = new DateTime(2024, 1, 4, 2, 0, 0); // Thursday 02:00

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_OvernightWindow_ScopedToTuesday_InactiveDuringTuesdayDaytime()
    {
        // Before the 21:00 start on Tuesday itself, the window has not begun yet.
        var window = CreateOvernightWindow(DayOfWeek.Tuesday);
        var localNow = new DateTime(2024, 1, 2, 12, 0, 0); // Tuesday noon

        Assert.False(window.IsActiveAt(localNow));
    }

    [Fact]
    public void IsActiveAt_NonWrappingWindow_StillWorksAsBefore()
    {
        // Sanity check the ordinary (non-wrapping) same-day path is unaffected.
        var window = new TimeWindow
        {
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Friday },
            StartTime = new TimeSpan(18, 0, 0),
            EndTime = new TimeSpan(22, 0, 0)
        };

        Assert.True(window.IsActiveAt(new DateTime(2024, 1, 5, 19, 0, 0))); // Friday 19:00
        Assert.False(window.IsActiveAt(new DateTime(2024, 1, 5, 23, 0, 0))); // Friday 23:00 (after end)
        Assert.False(window.IsActiveAt(new DateTime(2024, 1, 6, 19, 0, 0))); // Saturday 19:00 (wrong day)
    }
}
