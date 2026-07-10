namespace ScreenBux.Data.Entities;

/// <summary>
/// A short-lived, parent-generated code entered on a device to link it to an account.
/// </summary>
public class DeviceLinkCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public Guid? ChildProfileId { get; set; }

    /// <summary>
    /// Short human-enterable code (e.g. 6-8 characters).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Set once the code has been redeemed by a device so it cannot be reused.
    /// </summary>
    public DateTime? RedeemedAt { get; set; }

    public Guid? RedeemedByDeviceId { get; set; }
}
