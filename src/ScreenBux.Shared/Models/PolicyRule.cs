namespace ScreenBux.Shared.Models;

/// <summary>
/// What triggers a <see cref="PolicyRule"/>.
/// </summary>
public enum PolicyConditionKind
{
    /// <summary>Matches when a process/window matches <see cref="PolicyRule.ProcessNameRegex"/> or <see cref="PolicyRule.WindowTitleRegex"/>. Default for existing rules.</summary>
    ProcessMatch = 0,

    /// <summary>Always matches - evaluated once per policy tick, independent of any specific process. Used for whole-session actions like Sleep/Hibernate lockouts.</summary>
    Always = 1
}

/// <summary>
/// What happens when a <see cref="PolicyRule"/> matches.
/// </summary>
public enum PolicyRuleAction
{
    /// <summary>Gracefully close the matched process (today's default/implicit behavior).</summary>
    CloseProcess = 0,

    /// <summary>Forcefully terminate the matched process and its entire process tree.</summary>
    KillProcessTree = 1,

    /// <summary>Put the device to sleep.</summary>
    Sleep = 2,

    /// <summary>Hibernate the device; falls back to Sleep if hibernation is unavailable/fails.</summary>
    Hibernate = 3
}

/// <summary>
/// Represents a rule for closing processes based on regex matching, or a device-wide action
/// gated on an always-true condition (e.g. a "Sleep" lockout profile).
/// </summary>
public class PolicyRule
{
    public string Name { get; set; } = string.Empty;
    public string? ProcessNameRegex { get; set; }
    public string? WindowTitleRegex { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>What triggers this rule. Defaults to <see cref="PolicyConditionKind.ProcessMatch"/> for backward compatibility with existing stored policy JSON.</summary>
    public PolicyConditionKind ConditionKind { get; set; } = PolicyConditionKind.ProcessMatch;

    /// <summary>What happens when this rule matches. Defaults to <see cref="PolicyRuleAction.CloseProcess"/>, today's implicit behavior.</summary>
    public PolicyRuleAction Action { get; set; } = PolicyRuleAction.CloseProcess;
}
