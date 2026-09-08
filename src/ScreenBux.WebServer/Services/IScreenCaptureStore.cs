using System.Collections.Concurrent;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// A single monitor's screenshot, uploaded by the Service on behalf of a device.
/// </summary>
public record CachedCaptureImage(int Index, int WidthPx, int HeightPx, byte[] ImageBytes);

/// <summary>
/// A device's on-demand screen capture result, held in memory only until it is downloaded by
/// the parent (or it expires) - images are never persisted to disk or a database, since this is
/// a short-lived, one-off viewing feature.
/// </summary>
public record CachedCapture(string AccountId, Guid DeviceId, IReadOnlyList<CachedCaptureImage> Images, DateTime ExpiresAtUtc);

/// <summary>
/// Temporarily holds screen-capture images uploaded by the Service, keyed by RequestId, so the
/// WebClient can download them via a normal REST GET after being notified over SignalR that
/// they're ready. Entries expire on a flat timeout regardless of download status, to keep
/// cleanup simple; this is acceptable since captures are only ever fetched once, shortly after
/// the "ready" notification.
/// </summary>
public interface IScreenCaptureStore
{
    void Add(Guid requestId, CachedCapture capture);
    CachedCapture? Get(Guid requestId);
    void Remove(Guid requestId);
}

public class InMemoryScreenCaptureStore : IScreenCaptureStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<Guid, CachedCapture> _captures = new();

    public void Add(Guid requestId, CachedCapture capture)
    {
        PruneExpired();
        _captures[requestId] = capture;
    }

    public CachedCapture? Get(Guid requestId)
    {
        if (_captures.TryGetValue(requestId, out var capture))
        {
            if (capture.ExpiresAtUtc > DateTime.UtcNow)
            {
                return capture;
            }

            _captures.TryRemove(requestId, out _);
        }

        return null;
    }

    public void Remove(Guid requestId)
    {
        _captures.TryRemove(requestId, out _);
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, capture) in _captures)
        {
            if (capture.ExpiresAtUtc <= now)
            {
                _captures.TryRemove(key, out _);
            }
        }
    }

    public static TimeSpan DefaultTtl => Ttl;
}
