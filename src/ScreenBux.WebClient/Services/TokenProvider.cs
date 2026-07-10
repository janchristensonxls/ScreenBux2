namespace ScreenBux.WebClient.Services;

/// <summary>
/// Per-circuit holder for the current parent's JWT bearer token.
/// </summary>
public class TokenProvider
{
    public string? Token { get; set; }
    public string? Email { get; set; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public void Clear()
    {
        Token = null;
        Email = null;
    }
}
