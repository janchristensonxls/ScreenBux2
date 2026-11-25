namespace ScreenBux.Shared.Models;

/// <summary>
/// Everything needed to decide which mode (<see cref="PolicyProfileDto"/>) is in effect for one
/// scope at any moment: an optional temporary <see cref="Override"/>, a weekly
/// <see cref="Schedule"/>, and the <see cref="DefaultProfileId"/> used when neither applies.
/// Evaluated by <c>ModeResolver</c> - on the device every tick, and on the server for display.
/// See docs/decisions/mode-scheduling-and-temporary-overrides.md.
/// </summary>
public class ModeSelection
{
    /// <summary>The mode in effect when no override is active and no schedule entry matches.</summary>
    public Guid DefaultProfileId { get; set; }

    /// <summary>Evaluated in list order; the first enabled entry whose window matches wins.</summary>
    public List<ModeScheduleEntry> Schedule { get; set; } = new();

    /// <summary>A temporary (or pinned) switch that beats the schedule and the default while active.</summary>
    public ModeOverride? Override { get; set; }
}

/// <summary>"During <see cref="Window"/>, use mode <see cref="ProfileId"/>" - e.g. School Mon-Fri 08:00-15:00.</summary>
public class ModeScheduleEntry
{
    public Guid ProfileId { get; set; }

    /// <summary>Lets a parent turn an entry off temporarily (e.g. school holidays) without deleting it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Device-local recurring window; overnight ranges (22:00-06:00) wrap past midnight.</summary>
    public TimeWindow Window { get; set; } = new();
}

/// <summary>
/// A parent's manual mode switch. Ends at <see cref="ExpiresAtUtc"/>, or at the next change of
/// the scheduled/default mode when <see cref="EndsAtNextScheduleChange"/> is set, whichever comes
/// first. With neither set it is a pin that lasts until the parent removes it.
/// </summary>
public class ModeOverride
{
    public Guid ProfileId { get; set; }

    /// <summary>
    /// When the override was set. Only used as the starting point for
    /// <see cref="EndsAtNextScheduleChange"/>; it never delays activation.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Fixed end time (UTC). Null means no fixed end.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>End the first time the scheduled/default mode changes after <see cref="StartedAtUtc"/>.</summary>
    public bool EndsAtNextScheduleChange { get; set; }

    /// <summary>True when neither end condition is set - lasts until removed.</summary>
    public bool IsPinned => ExpiresAtUtc is null && !EndsAtNextScheduleChange;
}

/// <summary>Which layer decided the mode currently in effect.</summary>
public enum ModeSource
{
    Default,
    Schedule,
    Override
}

/// <summary>Result of resolving a <see cref="ModeSelection"/> at a point in time.</summary>
public class ModeResolution
{
    public Guid ProfileId { get; init; }

    public ModeSource Source { get; init; }

    /// <summary>Index into <see cref="ModeSelection.Schedule"/> when <see cref="Source"/> is <see cref="ModeSource.Schedule"/>.</summary>
    public int? ScheduleEntryIndex { get; init; }

    /// <summary>
    /// When the mode in effect is next expected to change (UTC), if known within the resolver's
    /// look-ahead. Null for a pin, or when nothing changes within the look-ahead.
    /// </summary>
    public DateTime? UntilUtc { get; init; }

    /// <summary>The mode that takes over at <see cref="UntilUtc"/>; null when <see cref="UntilUtc"/> is null.</summary>
    public Guid? NextProfileId { get; init; }
}

/// <summary>
/// What a device receives from the server: its flattened <see cref="ModeSelection"/> plus the
/// policy of every mode the selection can resolve to, so the device can switch modes on its own
/// (including while offline) without another round trip.
/// </summary>
public class DevicePolicyBundle
{
    public ModeSelection Selection { get; set; } = new();

    /// <summary>Policy per mode id. Each <see cref="PolicyConfiguration.Name"/> is the mode's name.</summary>
    public Dictionary<Guid, PolicyConfiguration> Profiles { get; set; } = new();
}
