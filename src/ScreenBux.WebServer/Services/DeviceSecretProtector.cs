using Microsoft.AspNetCore.DataProtection;

namespace ScreenBux.WebServer.Services;

public interface IDeviceSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

public class DeviceSecretProtector : IDeviceSecretProtector
{
    private readonly IDataProtector _protector;

    public DeviceSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ScreenBux.DeviceSecret.v1");
    }

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
