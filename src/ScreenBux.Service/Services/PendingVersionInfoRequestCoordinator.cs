using System.Collections.Concurrent;

namespace ScreenBux.Service.Services;

/// <summary>
/// Bridges the Service's on-demand "get version info" flow (triggered asynchronously via
/// SignalR, handled by <see cref="PolicySyncService"/>) with the Agent's periodic named-pipe
/// polling (handled by <see cref="NamedPipeServerService"/>). A pending request is registered
/// here, then piggybacked onto the next <see cref="Shared.Messages.CommandResponse"/> the Agent
/// receives; when the Agent's follow-up <see cref="Shared.Messages.VersionInfoReportMessage"/>
/// arrives, it completes the corresponding waiter. Mirrors <see cref="PendingWindowListRequestCoordinator"/>.
/// </summary>
public class PendingVersionInfoRequestCoordinator
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Shared.Messages.VersionInfoReportMessage?>> _pending = new();

    /// <summary>
    /// The oldest currently-pending request id, if any, to piggyback onto the next outgoing
    /// Agent response. Only one in-flight request is surfaced at a time; additional concurrent
    /// requests queue behind it and are picked up on subsequent ticks.
    /// </summary>
    public Guid? TryGetNextPendingRequestId()
    {
        return _pending.IsEmpty ? null : _pending.Keys.First();
    }

    /// <summary>
    /// Registers a new pending request immediately (synchronously), so it's visible to
    /// <see cref="TryGetNextPendingRequestId"/> as soon as possible - callers should register
    /// before doing any other (potentially slow) work, then call <see cref="WaitAsync"/>
    /// afterward, to give the Agent's next poll tick the maximum chance of picking it up
    /// before the wait times out.
    /// </summary>
    public void Register(Guid requestId)
    {
        _pending.TryAdd(requestId, new TaskCompletionSource<Shared.Messages.VersionInfoReportMessage?>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>
    /// Awaits the result of a previously <see cref="Register"/>-ed request, or times out.
    /// </summary>
    public async Task<Shared.Messages.VersionInfoReportMessage?> WaitAsync(Guid requestId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!_pending.TryGetValue(requestId, out var tcs))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await using var registration = cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Called when the Agent's <see cref="Shared.Messages.VersionInfoReportMessage"/> arrives.
    /// </summary>
    public void Complete(Guid requestId, Shared.Messages.VersionInfoReportMessage message)
    {
        if (_pending.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult(message);
        }
    }
}
