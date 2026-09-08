namespace ScreenBux.Shared.Models;

/// <summary>
/// A single monitor's screenshot captured by the Agent, encoded as JPEG bytes. Sent from the
/// Agent to the Service over the named pipe, then re-uploaded by the Service to the WebServer
/// over REST (not SignalR, to avoid pushing large binary payloads through the hub).
/// </summary>
public class CapturedImage
{
    public int Index { get; set; }
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public byte[] ImageBytes { get; set; } = [];
}
