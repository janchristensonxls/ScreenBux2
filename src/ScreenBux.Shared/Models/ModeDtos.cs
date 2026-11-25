namespace ScreenBux.Shared.Models;

/// <summary>
/// Body of <c>POST /api/policy/override</c>: switch to <see cref="ProfileId"/> now. Set at most one
/// of <see cref="DurationMinutes"/> / <see cref="ExpiresAtUtc"/>; combine with
/// <see cref="EndsAtNextScheduleChange"/> for "whichever comes first". None of them = pin.
/// </summary>
public class SetModeOverrideRequest
{
    public Guid ProfileId { get; set; }

    /// <summary>"For N minutes" - converted by the server to an absolute expiry.</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>"Until this time" (UTC).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public bool EndsAtNextScheduleChange { get; set; }
}

/// <summary>
/// Mode state of one scope - the account default (<see cref="ChildProfileId"/> null) or one
/// child - for the WebClient: what is configured, and which mode is in effect right now.
/// </summary>
public class ModeScopeStatusDto
{
    public Guid? ChildProfileId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>The default mode in effect for this scope (child's own, or the account default).</summary>
    public Guid DefaultProfileId { get; set; }

    /// <summary>
    /// This scope's own schedule. For a child, null means it follows the account schedule; an
    /// empty list means "no schedule" (explicitly not following the account's).
    /// </summary>
    public List<ModeScheduleEntry>? OwnSchedule { get; set; }

    /// <summary>The schedule actually used (own, or inherited from the account).</summary>
    public List<ModeScheduleEntry> EffectiveSchedule { get; set; } = new();

    /// <summary>This scope's own override, if any (may already have ended - see <see cref="Current"/>).</summary>
    public ModeOverride? OwnOverride { get; set; }

    /// <summary>The override actually used (own, or the account's "everyone" override).</summary>
    public ModeOverride? EffectiveOverride { get; set; }

    /// <summary>Time zone the schedule was evaluated in (from one of the scope's devices; "UTC" if none reported).</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public ModeResolution Current { get; set; } = new();
}
