using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.WebClient.Components.PolicyEditor;

public enum PolicyIssueSeverity
{
    Warning,
    Error
}

public sealed record PolicyIssue(PolicyIssueSeverity Severity, string Area, string Message);

/// <summary>Shared helpers for the visual policy editor (cloning, labels, validation).</summary>
public static class PolicyEditorHelpers
{
    public static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    public static readonly PolicyRuleAction[] ProcessActions = { PolicyRuleAction.CloseProcess, PolicyRuleAction.KillProcessTree };

    public static readonly PolicyRuleAction[] PowerActions = { PolicyRuleAction.Sleep, PolicyRuleAction.Hibernate, PolicyRuleAction.PowerOff };

    public static string Serialize(PolicyConfiguration policy) => JsonSerializer.Serialize(policy, PolicyJsonOptions.Default);

    public static PolicyConfiguration Clone(PolicyConfiguration policy) =>
        JsonSerializer.Deserialize<PolicyConfiguration>(Serialize(policy), PolicyJsonOptions.Default) ?? new PolicyConfiguration();

    public static string ShortDay(DayOfWeek day) => day.ToString()[..3];

    public static string ActionLabel(PolicyRuleAction action) => action switch
    {
        PolicyRuleAction.CloseProcess => "Close app",
        PolicyRuleAction.KillProcessTree => "Kill app (incl. child processes)",
        PolicyRuleAction.Sleep => "Sleep",
        PolicyRuleAction.Hibernate => "Hibernate",
        PolicyRuleAction.PowerOff => "Power off",
        _ => action.ToString()
    };

    public static string EnforcementLabel(CategoryPolicyEnforcement enforcement) => enforcement switch
    {
        CategoryPolicyEnforcement.Allowed => "Allowed",
        CategoryPolicyEnforcement.Blocked => "Blocked",
        CategoryPolicyEnforcement.TimeLimited => "Time limited",
        _ => enforcement.ToString()
    };

    public static string EnforcementBadge(CategoryPolicyEnforcement enforcement) => enforcement switch
    {
        CategoryPolicyEnforcement.Allowed => "bg-success",
        CategoryPolicyEnforcement.Blocked => "bg-danger",
        _ => "bg-warning text-dark"
    };

    public static string FormatMinutes(int? minutes)
    {
        if (minutes is null)
        {
            return "no limit";
        }

        var h = minutes.Value / 60;
        var m = minutes.Value % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    public static string FormatDays(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0 || days.Count == 7)
        {
            return "Every day";
        }

        var set = days.ToHashSet();
        if (set.SetEquals(WeekOrder.Take(5)))
        {
            return "Weekdays";
        }

        if (set.SetEquals(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }))
        {
            return "Weekend";
        }

        return string.Join(", ", WeekOrder.Where(set.Contains).Select(ShortDay));
    }

    public static string FormatWindow(TimeWindow window) =>
        $"{FormatDays(window.DaysOfWeek)} {window.StartTime:hh\\:mm}–{window.EndTime:hh\\:mm}{(window.EndTime < window.StartTime ? " (overnight)" : string.Empty)}";

    public static string? RegexError(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Same classification semantics as the Service: first enabled matching rule in list order wins.</summary>
    public static string ClassifyName(PolicyConfiguration policy, string processName, string? windowTitle)
    {
        foreach (var category in policy.AppCategories)
        {
            foreach (var rule in category.Rules)
            {
                if (RegexError(rule.ProcessNameRegex) is not null || RegexError(rule.WindowTitleRegex) is not null)
                {
                    continue;
                }

                if (rule.Matches(processName, windowTitle, isForegroundWindow: true))
                {
                    return category.Name;
                }
            }
        }

        return "Other";
    }

    public static List<PolicyIssue> Validate(PolicyConfiguration policy)
    {
        var issues = new List<PolicyIssue>();
        var names = policy.AppCategories.Select(c => c.Name).ToList();

        foreach (var dup in names.Where(n => !string.IsNullOrWhiteSpace(n)).GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            issues.Add(new(PolicyIssueSeverity.Error, "Categories", $"Category name \"{dup.Key}\" is used more than once."));
        }

        foreach (var category in policy.AppCategories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
            {
                issues.Add(new(PolicyIssueSeverity.Error, "Categories", "A category has no name."));
            }

            for (var i = 0; i < category.Rules.Count; i++)
            {
                var rule = category.Rules[i];
                if (RegexError(rule.ProcessNameRegex) is { } pErr)
                {
                    issues.Add(new(PolicyIssueSeverity.Error, "Categories", $"{category.Name}, rule {i + 1}: invalid process regex ({pErr})."));
                }

                if (RegexError(rule.WindowTitleRegex) is { } wErr)
                {
                    issues.Add(new(PolicyIssueSeverity.Error, "Categories", $"{category.Name}, rule {i + 1}: invalid window title regex ({wErr})."));
                }

                if (string.IsNullOrEmpty(rule.ProcessNameRegex) && string.IsNullOrEmpty(rule.WindowTitleRegex))
                {
                    issues.Add(new(PolicyIssueSeverity.Warning, "Categories", $"{category.Name}, rule {i + 1}: has no patterns and never matches."));
                }
            }
        }

        var known = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) { "Other" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < policy.CategoryPolicies.Count; i++)
        {
            var cp = policy.CategoryPolicies[i];
            var label = $"Policy {i + 1}";
            if (cp.CategoryNames.Count == 0)
            {
                issues.Add(new(PolicyIssueSeverity.Warning, "Category policies", $"{label}: no categories selected."));
            }

            foreach (var n in cp.CategoryNames)
            {
                if (!known.Contains(n))
                {
                    issues.Add(new(PolicyIssueSeverity.Warning, "Category policies", $"{label}: unknown category \"{n}\"."));
                }

                if (!seen.Add(n))
                {
                    issues.Add(new(PolicyIssueSeverity.Warning, "Category policies", $"{label}: \"{n}\" is already governed by an earlier policy (first match wins)."));
                }
            }

            if (cp.Enforcement == CategoryPolicyEnforcement.TimeLimited && (cp.DailyBudgetMinutes is null or <= 0))
            {
                issues.Add(new(PolicyIssueSeverity.Error, "Category policies", $"{label}: time limited but no daily budget set."));
            }

            if (cp.IdleTimeOutMode == IdleTimeOutMode.AfterNoInput && (cp.IdleTimeOut is null || cp.IdleTimeOut <= TimeSpan.Zero))
            {
                issues.Add(new(PolicyIssueSeverity.Error, "Category policies", $"{label}: idle time-out enabled but no duration set."));
            }
        }

        foreach (var rule in policy.SessionRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                issues.Add(new(PolicyIssueSeverity.Warning, "Session rules", "A session rule has no name."));
            }

            if (rule.Enabled && rule.Schedule.Count == 0)
            {
                issues.Add(new(PolicyIssueSeverity.Warning, "Session rules", $"\"{rule.Name}\" has no schedule and is ALWAYS active ({ActionLabel(rule.Action)} immediately)."));
            }

            if (rule.Schedule.Any(w => w.StartTime == w.EndTime))
            {
                issues.Add(new(PolicyIssueSeverity.Warning, "Session rules", $"\"{rule.Name}\" has a window with equal start and end time (never active)."));
            }
        }

        if (policy.CheckIntervalSeconds < 1)
        {
            issues.Add(new(PolicyIssueSeverity.Error, "General", "Check interval must be at least 1 second."));
        }

        if (policy.DayStartHour is < 0 or > 23)
        {
            issues.Add(new(PolicyIssueSeverity.Error, "General", "Day start hour must be 0–23."));
        }

        return issues;
    }

    /// <summary>Splits a window into same-day [start,end) hour segments per weekday, for the weekly schedule view.</summary>
    public static IEnumerable<(DayOfWeek Day, double StartHour, double EndHour)> Segments(TimeWindow window)
    {
        var days = window.DaysOfWeek.Count == 0 ? WeekOrder : window.DaysOfWeek.ToArray();
        var start = window.StartTime.TotalHours;
        var end = window.EndTime.TotalHours;
        foreach (var day in days)
        {
            if (end > start)
            {
                yield return (day, start, end);
            }
            else if (end < start)
            {
                yield return (day, start, 24);
                if (end > 0)
                {
                    yield return ((DayOfWeek)(((int)day + 1) % 7), 0, end);
                }
            }
        }
    }
}
