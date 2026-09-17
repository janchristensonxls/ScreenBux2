using System.Collections.Concurrent;
using ScreenBux.Shared.Models;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// A device's on-demand usage-log fetch result, held in memory only until it is downloaded by
/// the parent (or it expires) - not persisted to disk or a database, mirroring
/// <c>CachedCapture</c>/<see cref="IScreenCaptureStore"/> since this is the same kind of
/// short-lived, one-off viewing feature.
/// </summary>
public record CachedUsageLogs(string AccountId, Guid DeviceId, IReadOnlyList<UsageDayLog> Days, DateTime ExpiresAtUtc);

/// <summary>
/// Temporarily holds usage-log days uploaded by the Service, keyed by RequestId, so the
/// WebClient can download them via a normal REST GET after being notified over SignalR that
/// they're ready. Entries expire on a flat timeout regardless of download status, to keep
/// cleanup simple - acceptable since a result is only ever fetched once, shortly after the
/// "ready" notification.
/// </summary>
public interface IUsageLogsStore
{
    void Add(Guid requestId, CachedUsageLogs logs);
    CachedUsageLogs? Get(Guid requestId);
    void Remove(Guid requestId);
}

public class InMemoryUsageLogsStore : IUsageLogsStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, CachedUsageLogs> _results = new();

    public void Add(Guid requestId, CachedUsageLogs logs)
    {
        PruneExpired();
        _results[requestId] = logs;
    }

    public CachedUsageLogs? Get(Guid requestId)
    {
        if (_results.TryGetValue(requestId, out var logs))
        {
            if (logs.ExpiresAtUtc > DateTime.UtcNow)
            {
                return logs;
            }

            _results.TryRemove(requestId, out _);
        }

        return null;
    }

    public void Remove(Guid requestId)
    {
        _results.TryRemove(requestId, out _);
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, logs) in _results)
        {
            if (logs.ExpiresAtUtc <= now)
            {
                _results.TryRemove(key, out _);
            }
        }
    }

    public static TimeSpan DefaultTtl => Ttl;
}
