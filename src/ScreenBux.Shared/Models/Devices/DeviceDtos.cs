namespace ScreenBux.Shared.Models.Devices;

/// <summary>A parent-generated link code shown in the WebClient to enter on a device.</summary>
public class LinkCodeResponse
{
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Request from a device redeeming a link code to bind itself to an account.</summary>
public class RedeemLinkCodeRequest
{
    public string Code { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string MachineKey { get; set; } = string.Empty;
}

/// <summary>Response to a successful device link, containing the device-scoped token.</summary>
public class DeviceTokenResponse
{
    public Guid DeviceId { get; set; }
    public string Token { get; set; } = string.Empty;
}

/// <summary>Summary of a linked device for the parent's device list.</summary>
public class DeviceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ChildProfileId { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
