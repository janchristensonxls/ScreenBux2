namespace ScreenBux.Data.Entities;

/// <summary>
/// Pointer identifying which <see cref="PolicyProfile"/> ("mode") is currently active for a
/// given scope (account, and optionally a child profile or device). This is intentionally
/// just a pointer - it stores no policy content of its own; the actual serialized
/// <c>PolicyConfiguration</c> JSON lives solely on <see cref="ActivePolicyProfile"/>. This
/// guarantees there is exactly one place policy JSON can be edited, eliminating the
/// cache-desync bug where the old <c>PolicyJson</c> column could drift from the profile it
/// was supposed to mirror.
/// </summary>
public class PolicyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public Guid? ChildProfileId { get; set; }

    public Guid? DeviceId { get; set; }

    /// <summary>
    /// The <see cref="PolicyProfile"/> currently active for this scope. Should always be set
    /// in practice - <see cref="ScreenBux.WebServer.Services.EfPolicyStore"/> lazily creates a
    /// default "Normal" profile + document the first time an account is accessed with none.
    /// Kept nullable at the database level (rather than a required FK) to avoid
    /// insert-ordering issues when a document and its first profile are created together.
    /// </summary>
    public Guid? ActivePolicyProfileId { get; set; }

    public PolicyProfile? ActivePolicyProfile { get; set; }

    // --- Mode selection (docs/decisions/mode-scheduling-and-temporary-overrides.md) ---
    // Like the pointer above, these say *which* profile applies when; they hold no policy
    // content. A child-scoped document may exist only for these (ActivePolicyProfileId null
    // = the child follows the account default mode but has its own schedule/override).

    /// <summary>
    /// Mode of the temporary override, or null when there is none. An expired override may
    /// linger here until it is replaced or cleared; readers decide "active" via
    /// <c>ModeResolver</c>, never by this column being non-null.
    /// </summary>
    public Guid? OverrideProfileId { get; set; }

    public PolicyProfile? OverrideProfile { get; set; }

    /// <summary>When the override was set (UTC); start point for <see cref="OverrideEndsAtScheduleChange"/>.</summary>
    public DateTime? OverrideStartedAtUtc { get; set; }

    /// <summary>Fixed end of the override (UTC); null means no fixed end.</summary>
    public DateTime? OverrideExpiresAtUtc { get; set; }

    /// <summary>Override ends at the next change of the scheduled/default mode.</summary>
    public bool OverrideEndsAtScheduleChange { get; set; }

    /// <summary>
    /// Serialized <c>List&lt;ModeScheduleEntry&gt;</c> (PolicyJsonOptions); null or empty means
    /// no schedule for this scope.
    /// </summary>
    public string? ScheduleJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
