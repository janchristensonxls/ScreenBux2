using System.Collections.Concurrent;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Service.Services;

/// <summary>
/// In-memory FIFO of notifications waiting to be piggybacked onto the next CommandResponse the
/// Agent's periodic poll receives - mirrors <see cref="PendingWindowListRequestCoordinator"/> but
/// carries the full payload up front instead of a follow-up round trip, since a notification
/// (unlike a window list) has nothing further to fetch from the Agent.
/// </summary>
public class NotificationQueueService
{
    private readonly ConcurrentQueue<PendingNotification> _queue = new();

    /// <summary>Queues a notification for delivery on the Agent's next poll.</summary>
    public void Enqueue(PendingNotification notification) => _queue.Enqueue(notification);

    /// <summary>
    /// Dequeues the next notification to piggyback onto an outgoing CommandResponse, if any.
    /// Only one notification is surfaced per response; additional queued notifications are
    /// picked up on subsequent poll ticks.
    /// </summary>
    public bool TryDequeue(out PendingNotification? notification) => _queue.TryDequeue(out notification);
}
