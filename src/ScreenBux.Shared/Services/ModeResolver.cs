using ScreenBux.Shared.Models;

namespace ScreenBux.Shared.Services;

/// <summary>
/// Decides which mode is in effect for a <see cref="ModeSelection"/> at a given moment:
/// an active <see cref="ModeOverride"/> first, then the first matching enabled
/// <see cref="ModeScheduleEntry"/>, then <see cref="ModeSelection.DefaultProfileId"/>.
/// Pure and stateless - the device calls it every tick, the server calls it for display, and
/// both pass the device's time zone explicitly so they always agree.
/// See docs/decisions/mode-scheduling-and-temporary-overrides.md.
/// </summary>
public static class ModeResolver
{
    /// <summary>How far ahead <see cref="ModeResolution.UntilUtc"/> looks for the next change.</summary>
    public static readonly TimeSpan LookAhead = TimeSpan.FromDays(8);

    /// <param name="selection">The scope's override, schedule and default.</param>
    /// <param name="utcNow">Current time (UTC; Unspecified kind is treated as UTC).</param>
    /// <param name="timeZone">The device's time zone; schedule windows are in its local time.</param>
    /// <param name="isAvailable">
    /// Optional filter for profile ids that can actually be applied (e.g. present in the bundle).
    /// An override or schedule entry pointing at an unavailable profile is skipped.
    /// </param>
    public static ModeResolution Resolve(
        ModeSelection selection,
        DateTime utcNow,
        TimeZoneInfo timeZone,
        Func<Guid, bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(timeZone);
        isAvailable ??= static _ => true;

        utcNow = AsUtc(utcNow);
        var nowLocal = ToLocal(utcNow, timeZone);

        if (TryGetActiveOverride(selection, utcNow, nowLocal, timeZone, isAvailable, out var ov, out var overrideEndUtc))
        {
            return new ModeResolution
            {
                ProfileId = ov.ProfileId,
                Source = ModeSource.Override,
                UntilUtc = overrideEndUtc,
                NextProfileId = overrideEndUtc is DateTime end
                    ? ResolveUnderlying(selection, ToLocal(end, timeZone), isAvailable).ProfileId
                    : null
            };
        }

        var current = ResolveUnderlying(selection, nowLocal, isAvailable);
        var changeLocal = FindNextUnderlyingChange(selection, nowLocal, nowLocal + LookAhead, isAvailable);

        return new ModeResolution
        {
            ProfileId = current.ProfileId,
            Source = current.Source,
            ScheduleEntryIndex = current.Index,
            UntilUtc = changeLocal is DateTime c ? ToUtc(c, timeZone) : null,
            NextProfileId = changeLocal is DateTime c2 ? ResolveUnderlying(selection, c2, isAvailable).ProfileId : null
        };
    }

    /// <summary>True if <paramref name="selection"/>'s override is in effect at <paramref name="utcNow"/>.</summary>
    public static bool IsOverrideActive(ModeSelection selection, DateTime utcNow, TimeZoneInfo timeZone, Func<Guid, bool>? isAvailable = null) =>
        Resolve(selection, utcNow, timeZone, isAvailable).Source == ModeSource.Override;

    /// <summary>
    /// Resolves <paramref name="bundle"/> and returns the policy of the mode in effect, with
    /// <see cref="PolicyConfiguration.Name"/> set to the mode's name. Falls back to the default
    /// mode's policy, then to an empty policy, if the resolved mode isn't in the bundle.
    /// </summary>
    public static (PolicyConfiguration Policy, ModeResolution Resolution) ResolvePolicy(
        DevicePolicyBundle bundle, DateTime utcNow, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var resolution = Resolve(bundle.Selection, utcNow, timeZone, bundle.Profiles.ContainsKey);
        var policy = bundle.Profiles.TryGetValue(resolution.ProfileId, out var p) ? p
            : bundle.Profiles.TryGetValue(bundle.Selection.DefaultProfileId, out var d) ? d
            : new PolicyConfiguration();

        return (policy, resolution);
    }

    /// <summary>
    /// Looks up a time zone by id (as reported by a device's <c>TimeZoneInfo.Local.Id</c>),
    /// falling back to UTC when the id is missing or unknown on this machine.
    /// </summary>
    public static TimeZoneInfo FindTimeZoneOrUtc(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryGetActiveOverride(
        ModeSelection selection,
        DateTime utcNow,
        DateTime nowLocal,
        TimeZoneInfo timeZone,
        Func<Guid, bool> isAvailable,
        out ModeOverride activeOverride,
        out DateTime? endUtc)
    {
        activeOverride = null!;
        endUtc = null;

        var ov = selection.Override;
        if (ov is null || !isAvailable(ov.ProfileId))
        {
            return false;
        }

        DateTime? expiresUtc = ov.ExpiresAtUtc is DateTime e ? AsUtc(e) : null;
        if (expiresUtc is DateTime exp && exp <= utcNow)
        {
            return false;
        }

        DateTime? scheduleChangeUtc = null;
        if (ov.EndsAtNextScheduleChange)
        {
            // Clock skew between server (sets StartedAtUtc) and device must not make the scan
            // start in the future; clamp to now.
            var startLocal = ToLocal(AsUtc(ov.StartedAtUtc), timeZone);
            if (startLocal > nowLocal)
            {
                startLocal = nowLocal;
            }

            if (FindNextUnderlyingChange(selection, startLocal, nowLocal, isAvailable) is not null)
            {
                return false; // The schedule/default already changed since the override started.
            }

            var upcoming = FindNextUnderlyingChange(selection, nowLocal, nowLocal + LookAhead, isAvailable);
            scheduleChangeUtc = upcoming is DateTime u ? ToUtc(u, timeZone) : null;
        }

        activeOverride = ov;
        endUtc = Min(expiresUtc, scheduleChangeUtc);
        return true;
    }

    private static (Guid ProfileId, ModeSource Source, int? Index) ResolveUnderlying(
        ModeSelection selection, DateTime local, Func<Guid, bool> isAvailable)
    {
        for (var i = 0; i < selection.Schedule.Count; i++)
        {
            var entry = selection.Schedule[i];
            if (entry.Enabled && isAvailable(entry.ProfileId) && entry.Window.IsActiveAt(local))
            {
                return (entry.ProfileId, ModeSource.Schedule, i);
            }
        }

        return (selection.DefaultProfileId, ModeSource.Default, null);
    }

    /// <summary>
    /// First local time in (<paramref name="fromLocal"/>, <paramref name="limitLocal"/>] at which
    /// the schedule/default mode differs from the one in effect at <paramref name="fromLocal"/>.
    /// Only window start/end boundaries can change the result, so only those are checked.
    /// </summary>
    private static DateTime? FindNextUnderlyingChange(
        ModeSelection selection, DateTime fromLocal, DateTime limitLocal, Func<Guid, bool> isAvailable)
    {
        if (limitLocal <= fromLocal)
        {
            return null;
        }

        var baseline = ResolveUnderlying(selection, fromLocal, isAvailable).ProfileId;

        foreach (var boundary in EnumerateBoundaries(selection, fromLocal, limitLocal))
        {
            if (ResolveUnderlying(selection, boundary, isAvailable).ProfileId != baseline)
            {
                return boundary;
            }
        }

        return null;
    }

    private static IEnumerable<DateTime> EnumerateBoundaries(ModeSelection selection, DateTime fromLocal, DateTime limitLocal)
    {
        var boundaries = new SortedSet<DateTime>();

        // Start a day early so an overnight window that began yesterday contributes its end time.
        for (var day = fromLocal.Date.AddDays(-1); day <= limitLocal.Date; day = day.AddDays(1))
        {
            foreach (var entry in selection.Schedule)
            {
                var w = entry.Window;
                if (!entry.Enabled || w.StartTime == w.EndTime)
                {
                    continue;
                }

                var start = day + w.StartTime;
                var end = w.EndTime > w.StartTime ? day + w.EndTime : day.AddDays(1) + w.EndTime;

                foreach (var b in new[] { start, end })
                {
                    if (b > fromLocal && b <= limitLocal)
                    {
                        boundaries.Add(b);
                    }
                }
            }
        }

        return boundaries;
    }

    private static DateTime? Min(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (a < b ? a : b);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime ToLocal(DateTime utc, TimeZoneInfo timeZone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone), DateTimeKind.Unspecified);

    private static DateTime ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        // A boundary inside a DST spring-forward gap doesn't exist on the wall clock; it
        // effectively happens when the clock jumps, so move it past the gap.
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }
}
