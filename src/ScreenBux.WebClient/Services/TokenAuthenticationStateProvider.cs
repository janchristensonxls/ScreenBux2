using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Exposes the current parent's authentication state based on the per-circuit
/// <see cref="TokenProvider"/>.
/// </summary>
public class TokenAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly TokenProvider _tokenProvider;

    public TokenAuthenticationStateProvider(TokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity;
        if (_tokenProvider.IsAuthenticated)
        {
            identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, _tokenProvider.Email ?? "parent")
            }, authenticationType: "jwt");
        }
        else
        {
            identity = new ClaimsIdentity();
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void NotifyAuthenticationStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
