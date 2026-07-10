using System.Net.Http.Headers;

namespace ScreenBux.WebClient.Services;

/// <summary>
/// Attaches the current parent's JWT bearer token to outgoing API requests.
/// </summary>
public class BearerTokenHandler : DelegatingHandler
{
    private readonly TokenProvider _tokenProvider;

    public BearerTokenHandler(TokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_tokenProvider.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
