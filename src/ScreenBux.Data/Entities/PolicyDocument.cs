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

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
