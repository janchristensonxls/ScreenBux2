using System.Globalization;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Services;

namespace ScreenBux.WebClient.Components.PolicyEditor;

/// <summary>How long a mode switch made from the children strip lasts.</summary>
public enum ModeSwitchEnd
{
    /// <summary>For <see cref="ModeSwitchChoice.Minutes"/> minutes.</summary>
    Duration,
    /// <summary>Until <see cref="ModeSwitchChoice.UntilLocalTime"/> (next occurrence, scope's local time).</summary>
    UntilTime,
    /// <summary>Until the scheduled/default mode changes.</summary>
    NextScheduleChange,
    /// <summary>Temporary switch with no end - beats the schedule until ended.</summary>
    Pin,
    /// <summary>Not a temporary switch: changes the scope's default mode.</summary>
    Permanent
}

public sealed record ModeSwitchChoice(Guid ProfileId, ModeSwitchEnd End, int Minutes = 60, TimeSpan UntilLocalTime = default);

/// <summary>Formatting and checks shared by the mode strip and the schedule editor.</summary>
public static class ModeUi
{
    /// <summary>"15:00" today, "Mon 07:00" otherwise - in the scope's (device's) time zone.</summary>
    public static string FormatLocal(DateTime utc, string timeZoneId, DateTime nowUtc)
    {
        var timeZone = ModeResolver.FindTimeZoneOrUtc(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone);
        return local.Date == nowLocal.Date ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string SourceLabel(ModeSource source) => source switch
    {
        ModeSource.Override => "temporary",
        ModeSource.Schedule => "scheduled",
        _ => "default"
    };

    /// <summary>Converts a UI choice into an override request (null for <see cref="ModeSwitchEnd.Permanent"/>).</summary>
    public static SetModeOverrideRequest? ToRequest(ModeSwitchChoice choice, string timeZoneId, DateTime nowUtc)
    {
        switch (choice.End)
        {
            case ModeSwitchEnd.Duration:
                return new SetModeOverrideRequest { ProfileId = choice.ProfileId, DurationMinutes = choice.Minutes };
            case ModeSwitchEnd.UntilTime:
                var timeZone = ModeResolver.FindTimeZoneOrUtc(timeZoneId);
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone);
                var endLocal = nowLocal.Date + choice.UntilLocalTime;
                if (endLocal <= nowLocal)
                {
                    endLocal = endLocal.AddDays(1); // "until 07:00" after 07:00 means tomorrow.
                }

                while (timeZone.IsInvalidTime(endLocal))
                {
                    endLocal = endLocal.AddMinutes(30);
                }

                return new SetModeOverrideRequest
                {
                    ProfileId = choice.ProfileId,
                    ExpiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), timeZone)
                };
            case ModeSwitchEnd.NextScheduleChange:
                return new SetModeOverrideRequest { ProfileId = choice.ProfileId, EndsAtNextScheduleChange = true };
            case ModeSwitchEnd.Pin:
                return new SetModeOverrideRequest { ProfileId = choice.ProfileId };
            default:
                return null;
        }
    }

    /// <summary>
    /// Describes where enabled entries for different modes overlap, e.g. "Mon 09:00-10:00: Open
    /// and School". Sampled every 5 minutes over one week, which is exact for the 5-minute steps
    /// the editor allows.
    /// </summary>
    public static List<string> FindOverlaps(IReadOnlyList<ModeScheduleEntry> schedule, Func<Guid, string> nameOf)
    {
        var results = new List<string>();
        var monday = new DateTime(2024, 1, 1); // a Monday
        string? openKey = null;
        DateTime openStart = default;

        void Close(DateTime end)
        {
            if (openKey is not null)
            {
                var (first, second) = SplitKey(openKey);
                results.Add(string.Create(CultureInfo.InvariantCulture, $"{openStart:ddd HH:mm}-{end:HH:mm}: {nameOf(first)} and {nameOf(second)}"));
                openKey = null;
            }
        }

        for (var t = monday; t < monday.AddDays(7); t = t.AddMinutes(5))
        {
            var active = schedule.Where(e => e.Enabled && e.Window.IsActiveAt(t)).Select(e => e.ProfileId).Distinct().Take(2).ToList();
            var key = active.Count == 2 ? $"{active[0]}|{active[1]}" : null;
            if (key != openKey)
            {
                Close(t);
                if (key is not null)
                {
                    openKey = key;
                    openStart = t;
                }
            }
        }

        Close(monday.AddDays(7));
        return results;
    }

    private static (Guid, Guid) SplitKey(string key)
    {
        var parts = key.Split('|');
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }
}
