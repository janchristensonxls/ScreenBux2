namespace ScreenBux.Shared.Models;

/// <summary>
/// Broadcast to the parent's WebClient session via <c>MonitoringHub</c>'s account group when the
/// Agent acknowledges a <see cref="Messages.PendingNotification"/>. Mirrors the anonymous object
/// shape sent by <c>DevicesController.AcknowledgeNotification</c>.
/// </summary>
public class NotificationAcknowledgedDto
{
    public Guid DeviceId { get; set; }
    public Guid NotificationId { get; set; }
    public bool Shown { get; set; }
    public DateTime AcknowledgedAtUtc { get; set; }
}
