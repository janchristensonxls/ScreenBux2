namespace ScreenBux.Shared.Models.Devices;

/// <summary>A child profile, as returned to the parent's WebClient.</summary>
public class ChildProfileDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
}

/// <summary>Request to create a new child profile.</summary>
public class CreateChildProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Request to rename an existing child profile.</summary>
public class UpdateChildProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
}
