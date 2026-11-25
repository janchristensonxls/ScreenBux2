using ScreenBux.Shared.Models;
using ScreenBux.Shared.Services;

namespace ScreenBux.Service.Tests;

/// <summary>
/// Tests for <see cref="ModeResolver"/>: override &gt; schedule &gt; default, override end
/// conditions, and time-zone handling. Scenarios mirror
/// docs/decisions/mode-scheduling-and-temporary-overrides.md.
/// </summary>
public class ModeResolverTests
{
    private static readonly Guid Normal = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid School = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Sleep = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Open = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // 2024-01-02 is a Tuesday.
    private static DateTime Tue(int hour, int minute = 0) => new(2024, 1, 2, hour, minute, 0, DateTimeKind.Utc);
    private static DateTime Wed(int hour, int minute = 0) => new(2024, 1, 3, hour, minute, 0, DateTimeKind.Utc);
    private static DateTime Sat(int hour, int minute = 0) => new(2024, 1, 6, hour, minute, 0, DateTimeKind.Utc);

    private static ModeScheduleEntry Entry(Guid profileId, int startHour, int endHour, params DayOfWeek[] days) => new()
    {
        ProfileId = profileId,
        Window = new TimeWindow
        {
            DaysOfWeek = days.ToList(),
            StartTime = TimeSpan.FromHours(startHour),
            EndTime = TimeSpan.FromHours(endHour)
        }
    };

    private static readonly DayOfWeek[] Weekdays =
        { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    /// <summary>Default Normal; School Mon-Fri 08-15; Sleep every day 21-07.</summary>
    private static ModeSelection Family(ModeOverride? ov = null) => new()
    {
        DefaultProfileId = Normal,
        Schedule = { Entry(School, 8, 15, Weekdays), Entry(Sleep, 21, 7) },
        Override = ov
    };

    private static ModeOverride OverrideFor(Guid profileId, DateTime start, TimeSpan duration) => new()
    {
        ProfileId = profileId,
        StartedAtUtc = start,
        ExpiresAtUtc = start + duration
    };

    private static ModeOverride OverrideUntilNextChange(Guid profileId, DateTime start) => new()
    {
        ProfileId = profileId,
        StartedAtUtc = start,
        EndsAtNextScheduleChange = true
    };

    // --- No override: schedule and default ---

    [Fact]
    public void NoScheduleMatch_UsesDefault_UntilNextScheduledEntry()
    {
        var r = ModeResolver.Resolve(Family(), Tue(7, 30), Utc);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(ModeSource.Default, r.Source);
        Assert.Equal(Tue(8), r.UntilUtc);
        Assert.Equal(School, r.NextProfileId);
    }

    [Fact]
    public void ScheduleMatch_UsesScheduledMode_UntilWindowEnds()
    {
        var r = ModeResolver.Resolve(Family(), Tue(10), Utc);

        Assert.Equal(School, r.ProfileId);
        Assert.Equal(ModeSource.Schedule, r.Source);
        Assert.Equal(0, r.ScheduleEntryIndex);
        Assert.Equal(Tue(15), r.UntilUtc);
        Assert.Equal(Normal, r.NextProfileId);
    }

    [Fact]
    public void OvernightEntry_ActiveAfterMidnight_UntilMorning()
    {
        var r = ModeResolver.Resolve(Family(), Tue(22), Utc);

        Assert.Equal(Sleep, r.ProfileId);
        Assert.Equal(Wed(7), r.UntilUtc);
        Assert.Equal(Normal, r.NextProfileId);
    }

    [Fact]
    public void WeekdayOnlyEntry_NotActiveOnSaturday()
    {
        var r = ModeResolver.Resolve(Family(), Sat(10), Utc);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(Sat(21), r.UntilUtc);
        Assert.Equal(Sleep, r.NextProfileId);
    }

    [Fact]
    public void DisabledEntry_IsIgnored()
    {
        var selection = Family();
        selection.Schedule[0].Enabled = false;

        var r = ModeResolver.Resolve(selection, Tue(10), Utc);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(Tue(21), r.UntilUtc);
    }

    [Fact]
    public void OverlappingEntries_FirstMatchWins()
    {
        var selection = new ModeSelection
        {
            DefaultProfileId = Normal,
            Schedule = { Entry(Open, 9, 10), Entry(School, 8, 15) }
        };

        var r = ModeResolver.Resolve(selection, Tue(9, 30), Utc);

        Assert.Equal(Open, r.ProfileId);
        Assert.Equal(0, r.ScheduleEntryIndex);
        Assert.Equal(Tue(10), r.UntilUtc);
        Assert.Equal(School, r.NextProfileId);
    }

    [Fact]
    public void AdjacentEntriesWithSameMode_AreNotAChange()
    {
        var selection = new ModeSelection
        {
            DefaultProfileId = Normal,
            Schedule = { Entry(School, 8, 12), Entry(School, 12, 15) }
        };

        var r = ModeResolver.Resolve(selection, Tue(9), Utc);

        Assert.Equal(Tue(15), r.UntilUtc);
        Assert.Equal(Normal, r.NextProfileId);
    }

    [Fact]
    public void NoSchedule_DefaultHasNoEnd()
    {
        var r = ModeResolver.Resolve(new ModeSelection { DefaultProfileId = Normal }, Tue(10), Utc);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Null(r.UntilUtc);
        Assert.Null(r.NextProfileId);
    }

    [Fact]
    public void UnavailableScheduledProfile_FallsThroughToDefault()
    {
        var r = ModeResolver.Resolve(Family(), Tue(10), Utc, id => id != School);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(ModeSource.Default, r.Source);
    }

    // --- Overrides with a fixed duration ---

    [Fact]
    public void TimedOverride_ActiveUntilExpiry_ThenBackToDefault()
    {
        var selection = Family(OverrideFor(Open, Tue(16), TimeSpan.FromHours(1)));

        var during = ModeResolver.Resolve(selection, Tue(16, 30), Utc);
        Assert.Equal(Open, during.ProfileId);
        Assert.Equal(ModeSource.Override, during.Source);
        Assert.Equal(Tue(17), during.UntilUtc);
        Assert.Equal(Normal, during.NextProfileId);

        var after = ModeResolver.Resolve(selection, Tue(17), Utc);
        Assert.Equal(Normal, after.ProfileId);
        Assert.Equal(ModeSource.Default, after.Source);
    }

    [Fact]
    public void TimedOverride_EndsIntoWhatShouldBeActiveNow_NotWhatWasBefore()
    {
        // Started during School at 14:30; School ends at 15:00, so 15:30 lands in Normal.
        var selection = Family(OverrideFor(Open, Tue(14, 30), TimeSpan.FromHours(1)));

        var during = ModeResolver.Resolve(selection, Tue(15, 10), Utc);
        Assert.Equal(Open, during.ProfileId);
        Assert.Equal(Normal, during.NextProfileId);

        Assert.Equal(Normal, ModeResolver.Resolve(selection, Tue(15, 30), Utc).ProfileId);
    }

    [Fact]
    public void TimedOverride_BeatsScheduledSleep()
    {
        var selection = Family(OverrideFor(Open, Tue(20, 30), TimeSpan.FromHours(1)));

        var during = ModeResolver.Resolve(selection, Tue(21, 15), Utc);
        Assert.Equal(Open, during.ProfileId);
        Assert.Equal(Sleep, during.NextProfileId);

        Assert.Equal(Sleep, ModeResolver.Resolve(selection, Tue(21, 30), Utc).ProfileId);
    }

    [Fact]
    public void UnavailableOverrideProfile_IsIgnored()
    {
        var selection = Family(OverrideFor(Open, Tue(16), TimeSpan.FromHours(1)));

        var r = ModeResolver.Resolve(selection, Tue(16, 30), Utc, id => id != Open);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(ModeSource.Default, r.Source);
    }

    [Fact]
    public void OverrideStartedInFuture_ClockSkew_IsStillActive()
    {
        var selection = Family(OverrideFor(Open, Tue(16, 0), TimeSpan.FromHours(1)));

        var r = ModeResolver.Resolve(selection, Tue(15, 59), Utc);

        Assert.Equal(Open, r.ProfileId);
    }

    // --- Overrides until the next scheduled change ---

    [Fact]
    public void UntilNextChange_EndsWhenScheduleChanges()
    {
        var selection = Family(OverrideUntilNextChange(Open, Tue(20, 30)));

        var during = ModeResolver.Resolve(selection, Tue(20, 59), Utc);
        Assert.Equal(Open, during.ProfileId);
        Assert.Equal(Tue(21), during.UntilUtc);
        Assert.Equal(Sleep, during.NextProfileId);

        Assert.Equal(Sleep, ModeResolver.Resolve(selection, Tue(21), Utc).ProfileId);
    }

    [Fact]
    public void UntilNextChange_DoesNotComeBackWhenScheduleReturnsToStartingMode()
    {
        // Started in Normal at 16:00; Sleep at 21:00 ends it. Next morning is Normal again,
        // which must not revive the override.
        var selection = Family(OverrideUntilNextChange(Open, Tue(16)));

        var r = ModeResolver.Resolve(selection, Wed(7, 30), Utc);

        Assert.Equal(Normal, r.ProfileId);
        Assert.Equal(ModeSource.Default, r.Source);
    }

    [Fact]
    public void UntilNextChange_WithNoSchedule_NeverEnds()
    {
        var selection = new ModeSelection
        {
            DefaultProfileId = Normal,
            Override = OverrideUntilNextChange(Open, Tue(10))
        };

        var r = ModeResolver.Resolve(selection, Tue(10).AddDays(30), Utc);

        Assert.Equal(Open, r.ProfileId);
        Assert.Null(r.UntilUtc);
    }

    [Fact]
    public void UntilNextChange_AndExpiry_EarlierWins()
    {
        var ov = OverrideUntilNextChange(Open, Tue(20));
        ov.ExpiresAtUtc = Tue(20, 30);
        var selection = Family(ov);

        var during = ModeResolver.Resolve(selection, Tue(20, 10), Utc);
        Assert.Equal(Tue(20, 30), during.UntilUtc);

        Assert.Equal(Normal, ModeResolver.Resolve(selection, Tue(20, 30), Utc).ProfileId);
    }

    // --- Inherited (account-wide) override behind a child's own ---

    [Fact]
    public void InheritedOverride_ResumesWhenOwnOverrideEnds()
    {
        // 20:00 everyone pinned to Sleep; 20:30 the child gets "Open for 30 min".
        var selection = Family(OverrideFor(Open, Tue(20, 30), TimeSpan.FromMinutes(30)));
        selection.InheritedOverride = new ModeOverride { ProfileId = Sleep, StartedAtUtc = Tue(20) };

        var during = ModeResolver.Resolve(selection, Tue(20, 45), Utc);
        Assert.Equal(Open, during.ProfileId);
        Assert.False(during.OverrideIsInherited);
        Assert.Equal(Tue(21), during.UntilUtc);
        Assert.Equal(Sleep, during.NextProfileId);

        // After the child's override: back to the pinned Sleep, not the schedule/default.
        var after = ModeResolver.Resolve(selection, Tue(21, 0), Utc);
        Assert.Equal(Sleep, after.ProfileId);
        Assert.Equal(ModeSource.Override, after.Source);
        Assert.True(after.OverrideIsInherited);
        Assert.Null(after.UntilUtc);

        // Still Sleep the next morning, when the schedule alone would say Normal.
        Assert.Equal(Sleep, ModeResolver.Resolve(selection, Wed(10), Utc).ProfileId);
    }

    [Fact]
    public void InheritedOverride_EndedToo_FallsBackToScheduleAndDefault()
    {
        var selection = Family(OverrideFor(Open, Tue(16), TimeSpan.FromMinutes(30)));
        selection.InheritedOverride = OverrideFor(School, Tue(16), TimeSpan.FromHours(1));

        Assert.Equal(School, ModeResolver.Resolve(selection, Tue(16, 45), Utc).ProfileId);

        var after = ModeResolver.Resolve(selection, Tue(17), Utc);
        Assert.Equal(Normal, after.ProfileId);
        Assert.Equal(ModeSource.Default, after.Source);
    }

    // --- Pin ---

    [Fact]
    public void Pin_IgnoresScheduleIndefinitely()
    {
        var selection = Family(new ModeOverride { ProfileId = Open, StartedAtUtc = Tue(10) });

        Assert.True(selection.Override!.IsPinned);

        var r = ModeResolver.Resolve(selection, Tue(22).AddDays(14), Utc);

        Assert.Equal(Open, r.ProfileId);
        Assert.Equal(ModeSource.Override, r.Source);
        Assert.Null(r.UntilUtc);
        Assert.Null(r.NextProfileId);
    }

    // --- Time zones ---

    [Fact]
    public void ScheduleIsEvaluatedInDeviceTimeZone()
    {
        var plusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

        // 07:30 UTC is 08:30 local -> School, ending 15:00 local = 14:00 UTC.
        var r = ModeResolver.Resolve(Family(), Tue(7, 30), plusOne);

        Assert.Equal(School, r.ProfileId);
        Assert.Equal(Tue(14), r.UntilUtc);
    }

    [Fact]
    public void BoundaryInsideDstGap_IsReportedWhenClockJumps()
    {
        // UTC+1, DST (+1h) from last Sunday of March 02:00 local - 2024-03-31 02:00-03:00 doesn't exist.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        var cet = TimeZoneInfo.CreateCustomTimeZone("TestCET", TimeSpan.FromHours(1), "TestCET", "TestCET", "TestCEST", new[] { rule });

        var selection = new ModeSelection
        {
            DefaultProfileId = Normal,
            Schedule =
            {
                new ModeScheduleEntry
                {
                    ProfileId = Sleep,
                    Window = new TimeWindow { StartTime = new TimeSpan(2, 30, 0), EndTime = new TimeSpan(5, 0, 0) }
                }
            }
        };

        // 2024-03-30 22:00 UTC = 23:00 local (standard time).
        var r = ModeResolver.Resolve(selection, new DateTime(2024, 3, 30, 22, 0, 0, DateTimeKind.Utc), cet);

        Assert.Equal(Normal, r.ProfileId);
        // 02:30 doesn't exist; the clock jumps to 03:00 CEST = 01:00 UTC.
        Assert.Equal(new DateTime(2024, 3, 31, 1, 0, 0, DateTimeKind.Utc), r.UntilUtc);
        Assert.Equal(Sleep, r.NextProfileId);
    }
}
