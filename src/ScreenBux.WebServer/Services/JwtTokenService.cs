using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Issues JWT bearer tokens for parent accounts and linked devices.
/// </summary>
public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Issues an access token for a parent account.
    /// </summary>
    public string CreateAccountToken(string accountId, string email)
    {
        var jwt = _configuration.GetSection("Jwt");
        var minutes = int.TryParse(jwt["AccessTokenLifetimeMinutes"], out var m) ? m : 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, accountId),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.NameIdentifier, accountId),
            new("account_id", accountId),
            new("token_type", "account")
        };

        return CreateToken(claims, TimeSpan.FromMinutes(minutes));
    }

    /// <summary>
    /// Issues a long-lived token for a linked device, scoped to its owning account.
    /// </summary>
    public string CreateDeviceToken(Guid deviceId, string accountId)
    {
        var jwt = _configuration.GetSection("Jwt");
        var days = int.TryParse(jwt["DeviceTokenLifetimeDays"], out var d) ? d : 365;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceId.ToString()),
            new(ClaimTypes.NameIdentifier, deviceId.ToString()),
            new("account_id", accountId),
            new("device_id", deviceId.ToString()),
            new("token_type", "device")
        };

        return CreateToken(claims, TimeSpan.FromDays(days));
    }

    private string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var jwt = _configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
