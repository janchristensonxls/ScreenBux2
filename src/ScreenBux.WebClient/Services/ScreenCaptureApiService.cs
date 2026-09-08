using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ScreenBux.WebClient.Services;

/// <summary>Metadata for one downloaded image within a completed screen capture.</summary>
public record ScreenCaptureImageMetadata(int Index, int WidthPx, int HeightPx);

/// <summary>
/// Calls the WebServer's screen-capture endpoints to download images once notified (via
/// <see cref="MonitoringService.ScreenCaptureReady"/>) that a request has completed.
/// </summary>
public class ScreenCaptureApiService
{
    private readonly HttpClient _httpClient;

    public ScreenCaptureApiService(HttpClient httpClient, TokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        if (!string.IsNullOrEmpty(tokenProvider.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenProvider.Token);
        }
    }

    public async Task<IReadOnlyList<ScreenCaptureImageMetadata>> GetMetadataAsync(Guid requestId)
    {
        var metadata = await _httpClient.GetFromJsonAsync<List<ScreenCaptureImageMetadata>>($"api/screencapture/{requestId}");
        return metadata ?? new List<ScreenCaptureImageMetadata>();
    }

    public async Task<byte[]> DownloadImageAsync(Guid requestId, int index)
    {
        return await _httpClient.GetByteArrayAsync($"api/screencapture/{requestId}/{index}");
    }
}
