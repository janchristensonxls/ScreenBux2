using System.ComponentModel.DataAnnotations;

namespace ScreenBux.WebServer.Models;

public class PairingCode
{
    [Key]
    public string Code { get; set; } = string.Empty;

    public Guid HouseholdId { get; set; }
    public Household? Household { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
