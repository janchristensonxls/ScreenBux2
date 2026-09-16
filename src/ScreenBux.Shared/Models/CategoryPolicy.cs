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
/// Whether a <see cref="CategoryPolicy"/>'s usage accumulation is affected by keyboard/mouse
/// idle time. Deliberately per-category rather than a single global switch: lack of input is
/// often a legitimate, deliberate way to use some categories (e.g. watching a video or listening
/// to music) but a good signal of "walked away" for others (e.g. a game left paused). See
/// docs/decisions/screen-time-usage-tracking.md.
/// </summary>
public enum IdleTimeOutMode
{
    /// <summary>Usage keeps accumulating regardless of input activity (the default).</summary>
    Never = 0,

    /// <summary>Usage stops accumulating once there has been no keyboard/mouse input for at least <see cref="CategoryPolicy.IdleTimeOut"/>.</summary>
    AfterNoInput = 1
}

/// <summary>
/// Per-mode enforcement for one or more <c>AppCategory</c> entries (identified by name - see
/// docs/decisions/group-based-policy-model.md for why category identity flows by name rather
/// than a synced database id). One list of these lives on each active <see cref="PolicyConfiguration"/>,
/// resolved server-side for whichever <c>PolicyProfile</c>/mode is currently active.
///
/// A single policy can govern multiple fine-grained categories (e.g. a "Distraction" bucket
/// grouping "Games", "Social Media", and "YouTube") without merging their classification rules -
/// each category still classifies independently and accumulates its own usage seconds; this
/// policy's <see cref="DailyBudgetMinutes"/> is checked against the *sum* of usage across all of
/// <see cref="CategoryNames"/>. If the same category name appears in more than one
/// <see cref="CategoryPolicy"/> on the same profile, the first matching policy wins (in list
/// order) - this mirrors the existing first-match-wins semantics used for
/// <c>AppCategoryRule</c> classification.
/// </summary>
public class CategoryPolicy
{
    /// <summary>
    /// Names of the <c>AppCategory</c> entries this policy governs (e.g. ["Games"], or
    /// ["Games", "Social Media", "YouTube"] for a combined "Distraction" bucket).
    /// </summary>
    public List<string> CategoryNames { get; set; } = new();

    public CategoryPolicyEnforcement Enforcement { get; set; } = CategoryPolicyEnforcement.Allowed;

    /// <summary>Only meaningful when <see cref="Enforcement"/> is <see cref="CategoryPolicyEnforcement.TimeLimited"/>.</summary>
    public int? DailyBudgetMinutes { get; set; }

    /// <summary>What to do when this category is Blocked, or TimeLimited and its budget is exhausted.</summary>
    public PolicyRuleAction Action { get; set; } = PolicyRuleAction.CloseProcess;

    /// <summary>
    /// Whether keyboard/mouse idle time pauses usage accumulation for this category. Default
    /// Never - see <see cref="IdleTimeOutMode"/> for why this isn't a global setting.
    /// </summary>
    public IdleTimeOutMode IdleTimeOutMode { get; set; } = IdleTimeOutMode.Never;

    /// <summary>
    /// Only meaningful when <see cref="IdleTimeOutMode"/> is <see cref="ScreenBux.Shared.Models.IdleTimeOutMode.AfterNoInput"/>:
    /// how long the user must be idle (no keyboard/mouse input) before usage stops accumulating
    /// for this category.
    /// </summary>
    public TimeSpan? IdleTimeOut { get; set; }

    /// <summary>
    /// If non-empty, this category is only ever Allowed/TimeLimited within one of these
    /// windows; outside all of them it is treated as Blocked regardless of <see cref="Enforcement"/>.
    /// The daily budget (if any) is one day-wide total, not reset per window occurrence.
    /// </summary>
    public List<TimeWindow> AllowedWindows { get; set; } = new();
}
