using System.Security.Claims;

namespace ScreenBux.WebServer.Services;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Gets the account id from the JWT (works for both account and device tokens).</summary>
    public static string? GetAccountId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("account_id");

    /// <summary>Gets the device id from a device-scoped JWT, if present.</summary>
    public static Guid? GetDeviceId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("device_id");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
