using Microsoft.Extensions.Caching.Memory;

namespace ScreenBux.WebServer.Services;

public interface IDeviceNonceStore
{
    bool TryRegisterNonce(Guid deviceId, string nonce, DateTime expiresAt);
}

public class DeviceNonceStore : IDeviceNonceStore
{
    private readonly IMemoryCache _cache;

    public DeviceNonceStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryRegisterNonce(Guid deviceId, string nonce, DateTime expiresAt)
    {
        var cacheKey = $"device-nonce:{deviceId}:{nonce}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return false;
        }

        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return false;
        }

        _cache.Set(cacheKey, true, ttl);
        return true;
    }
}
