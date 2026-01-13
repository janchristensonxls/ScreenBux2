using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ScreenBux.WebServer.Services;

public interface IDeviceTokenService
{
    (string Token, DateTime ExpiresAt) CreateDeviceToken(Guid deviceId, Guid householdId, string clientType);
}

public class DeviceTokenService : IDeviceTokenService
{
    private readonly IConfiguration _configuration;

    public DeviceTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAt) CreateDeviceToken(Guid deviceId, Guid householdId, string clientType)
    {
        var issuer = _configuration["Jwt:Issuer"] ?? "ScreenBux";
        var audience = _configuration["Jwt:Audience"] ?? "ScreenBuxDevices";
        var signingKey = _configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt signing key missing");

        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"device:{deviceId}"),
            new("role", "device"),
            new("deviceId", deviceId.ToString()),
            new("householdId", householdId.ToString()),
            new("clientType", clientType)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
