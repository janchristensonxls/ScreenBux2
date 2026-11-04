using System.Globalization;
using ScreenBux.Shared.Messages;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Builds the <see cref="UsageStatusResponse"/> the Agent shows as "today" status: usage vs.
/// limits of the active policy, plus the next scheduled session action.
/// </summary>
public static class UsageStatusBuilder
{
    public static UsageStatusResponse Build(PolicyConfiguration config, UsageTrackingService usage, DateTime localNow)
    {
        var response = new UsageStatusResponse
        {
            PolicyName = config.Name,
            TrackUsage = config.TrackUsage,
            UsedSecondsToday = usage.GetDisplaySecondsToday(),
            DailyBudgetMinutes = config.DailyBudgetMinutes,
            DailyBudgetExceededAction = config.DailyBudgetExceededAction.ToString(),
            NextEvent = DescribeNextSessionEvent(config.SessionRules, localNow)
        };

        foreach (var policy in config.CategoryPolicies)
        {
            if (policy.Enforcement == CategoryPolicyEnforcement.Allowed && policy.AllowedWindows.Count == 0)
            {
                continue; // Unrestricted - nothing worth showing.
            }

            var used = usage.GetDisplaySecondsToday(policy.CategoryNames);
            response.Categories.Add(new UsageStatusCategoryItem
            {
                Label = string.Join(", ", policy.CategoryNames),
                Enforcement = policy.Enforcement.ToString(),
                UsedSeconds = used,
                BudgetMinutes = policy.Enforcement == CategoryPolicyEnforcement.TimeLimited ? policy.DailyBudgetMinutes : null,
                IsBlockedNow = PolicyService.IsBlockedByPolicy(policy, used),
                WindowNote = policy.Enforcement == CategoryPolicyEnforcement.Blocked ? null : DescribeWindows(policy.AllowedWindows, localNow)
            });
        }

        return response;
    }

    /// <summary>"allowed until 22:00" while inside a window, else "allowed from Fri 18:00"; null if no windows.</summary>
    private static string? DescribeWindows(List<TimeWindow> windows, DateTime localNow)
    {
        if (windows.Count == 0)
        {
            return null;
        }

        var activeEnd = windows.Where(w => w.IsActiveAt(localNow)).Select(w => CurrentEnd(w, localNow)).OrderByDescending(e => e).FirstOrDefault();
        if (activeEnd != default)
        {
            return $"allowed until {FormatWhen(activeEnd, localNow)}";
        }

        var nextStart = windows.Select(w => NextStart(w, localNow)).Where(s => s is not null).Min();
        return nextStart is DateTime s ? $"allowed from {FormatWhen(s, localNow)}" : null;
    }

    private static string? DescribeNextSessionEvent(List<SessionRule> rules, DateTime localNow)
    {
        // An ongoing rule takes precedence (e.g. used during a bedtime grant).
        var active = rules.FirstOrDefault(r => r.IsActiveAt(localNow));
        if (active is not null)
        {
            var end = active.Schedule.Where(w => w.IsActiveAt(localNow)).Select(w => CurrentEnd(w, localNow)).DefaultIfEmpty().Max();
            return end == default
                ? $"{active.Name} ({active.Action}) is active"
                : $"{active.Name} ({active.Action}) until {FormatWhen(end, localNow)}";
        }

        (SessionRule Rule, DateTime Start)? next = null;
        foreach (var rule in rules.Where(r => r.Enabled))
        {
            foreach (var window in rule.Schedule)
            {
                if (NextStart(window, localNow) is DateTime start && (next is null || start < next.Value.Start))
                {
                    next = (rule, start);
                }
            }
        }

        return next is { } n ? $"{n.Rule.Name} ({n.Rule.Action}) at {FormatWhen(n.Start, localNow)}" : null;
    }

    private static DateTime? NextStart(TimeWindow window, DateTime localNow)
    {
        for (var d = 0; d <= 7; d++)
        {
            var start = localNow.Date.AddDays(d) + window.StartTime;
            if (start > localNow && (window.DaysOfWeek.Count == 0 || window.DaysOfWeek.Contains(start.DayOfWeek)))
            {
                return start;
            }
        }

        return null;
    }

    /// <summary>End of the occurrence of <paramref name="window"/> that is active at <paramref name="localNow"/>.</summary>
    private static DateTime CurrentEnd(TimeWindow window, DateTime localNow)
    {
        var endToday = localNow.Date + window.EndTime;
        // Overnight window entered before midnight ends tomorrow.
        return window.EndTime < window.StartTime && localNow.TimeOfDay >= window.StartTime ? endToday.AddDays(1) : endToday;
    }

    private static string FormatWhen(DateTime when, DateTime localNow)
    {
        var time = when.ToString("HH:mm", CultureInfo.InvariantCulture);
        return when.Date == localNow.Date
            ? time
            : when.Date == localNow.Date.AddDays(1)
                ? $"tomorrow {time}"
                : $"{when.ToString("ddd", CultureInfo.InvariantCulture)} {time}";
    }
}
