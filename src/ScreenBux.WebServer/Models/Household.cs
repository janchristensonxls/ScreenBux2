namespace ScreenBux.WebServer.Models;

public class Household
{
    public Guid HouseholdId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserHousehold> UserHouseholds { get; set; } = new List<UserHousehold>();
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
