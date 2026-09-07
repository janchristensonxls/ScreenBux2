namespace ScreenBux.Shared.Models;

/// <summary>
/// Correlated response to an on-demand "get process list" request for a specific device,
/// relayed from the Service back through the WebServer hub to the requesting WebClient.
/// </summary>
public class ProcessListResult
{
    /// <summary>Matches the RequestId supplied by the WebClient when requesting the list.</summary>
    public Guid RequestId { get; set; }

    public Guid DeviceId { get; set; }

    public bool Success { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public List<ProcessInfo> Processes { get; set; } = new();
}
