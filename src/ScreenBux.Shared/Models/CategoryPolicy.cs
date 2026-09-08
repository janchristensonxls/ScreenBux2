namespace ScreenBux.Shared.Models;

/// <summary>
/// What happens when a <see cref="CategoryPolicy"/> or <see cref="SessionRule"/> fires.
/// Carried over from the retired <c>PolicyRule.PolicyRuleAction</c>.
/// </summary>
public enum PolicyRuleAction
{
    /// <summary>Gracefully close the matched process.</summary>
    CloseProcess = 0,

    /// <summary>Forcefully terminate the matched process and its entire process tree.</summary>
    KillProcessTree = 1,

    /// <summary>Put the device to sleep.</summary>
    Sleep = 2,

    /// <summary>Hibernate the device; falls back to Sleep if hibernation is unavailable/fails.</summary>
    Hibernate = 3
}

/// <summary>How a <see cref="CategoryPolicy"/> treats its category in the active mode.</summary>
public enum CategoryPolicyEnforcement
{
    /// <summary>No restriction; processes in this category always run.</summary>
    Allowed = 0,

    /// <summary>Always closed/killed, regardless of usage.</summary>
    Blocked = 1,

    /// <summary>Allowed until <see cref="CategoryPolicy.DailyBudgetMinutes"/> is exhausted for the day, then treated as Blocked.</summary>
    TimeLimited = 2
}

/// <summary>
/// Per-mode enforcement for one <c>AppCategory</c> (identified by name - see
/// docs/decisions/group-based-policy-model.md for why category identity flows by name rather
/// than a synced database id). One list of these lives on each active <see cref="PolicyConfiguration"/>,
/// resolved server-side for whichever <c>PolicyProfile</c>/mode is currently active.
/// </summary>
public class CategoryPolicy
{
    /// <summary>Name of the <c>AppCategory</c> this policy governs (e.g. "Games", "Social Media", "Other").</summary>
    public string CategoryName { get; set; } = string.Empty;

    public CategoryPolicyEnforcement Enforcement { get; set; } = CategoryPolicyEnforcement.Allowed;

    /// <summary>Only meaningful when <see cref="Enforcement"/> is <see cref="CategoryPolicyEnforcement.TimeLimited"/>.</summary>
    public int? DailyBudgetMinutes { get; set; }

    /// <summary>What to do when this category is Blocked, or TimeLimited and its budget is exhausted.</summary>
    public PolicyRuleAction Action { get; set; } = PolicyRuleAction.CloseProcess;

    /// <summary>
    /// If non-empty, this category is only ever Allowed/TimeLimited within one of these
    /// windows; outside all of them it is treated as Blocked regardless of <see cref="Enforcement"/>.
    /// The daily budget (if any) is one day-wide total, not reset per window occurrence.
    /// </summary>
    public List<TimeWindow> AllowedWindows { get; set; } = new();
}
