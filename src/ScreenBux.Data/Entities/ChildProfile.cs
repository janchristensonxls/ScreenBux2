namespace ScreenBux.Data.Entities;

/// <summary>
/// A person (child) whose time budget can span multiple devices.
/// </summary>
public class ChildProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountId { get; set; } = string.Empty;

    public Account? Account { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
