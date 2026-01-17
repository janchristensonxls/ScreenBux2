namespace ScreenBux.WebServer.Models;

public class Device
{
    public Guid DeviceId { get; set; }
    public Guid HouseholdId { get; set; }
    public Household? Household { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string EncryptedSecret { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public bool IsRevoked { get; set; }

    public string? AgentVersion { get; set; }
    public string? ServiceVersion { get; set; }
    public string? MachineName { get; set; }
    public string? OsVersion { get; set; }

    public bool OnlineAgent { get; set; }
    public bool OnlineService { get; set; }
}
