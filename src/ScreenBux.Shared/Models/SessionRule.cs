namespace ScreenBux.Shared.Models;

/// <summary>
/// A whole-session action (e.g. a bedtime lockout) not tied to any specific app category -
/// the surviving purpose of the retired <c>PolicyRule.ConditionKind.Always</c>. Evaluated once
/// per policy tick, independent of any running process.
/// </summary>
public class SessionRule
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public PolicyRuleAction Action { get; set; } = PolicyRuleAction.Sleep;

    /// <summary>
    /// When this rule fires. An empty/null schedule means "always" (today's unconditional
    /// Always-rule behavior, preserved for backward compatibility); when non-empty, the rule
    /// only fires while device-local time falls within one of the windows.
    /// </summary>
    public List<TimeWindow> Schedule { get; set; } = new();

    /// <summary>True if this rule is currently triggered, given device-local time.</summary>
    public bool IsActiveAt(DateTime localNow)
    {
        if (!Enabled)
        {
            return false;
        }

        return Schedule.Count == 0 || Schedule.Any(w => w.IsActiveAt(localNow));
    }
}
