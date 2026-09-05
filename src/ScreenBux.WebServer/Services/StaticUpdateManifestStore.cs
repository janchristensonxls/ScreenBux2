using ScreenBux.Shared.Models.Updates;

namespace ScreenBux.WebServer.Services;

/// <summary>
/// Configuration-backed <see cref="IUpdateManifestStore"/>. Reads the latest version/download
/// URL for each component from the "Updates" section of appsettings.json (or any other
/// configuration provider). This is a placeholder for a real release feed/CI pipeline - it
/// lets the rest of the auto-update plumbing (Updater service, controller, DTOs) be complete
/// and testable before that pipeline exists; swapping in a database- or feed-backed
/// implementation later only requires a new <see cref="IUpdateManifestStore"/> implementation.
/// </summary>
public class StaticUpdateManifestStore : IUpdateManifestStore
{
    private readonly IConfiguration _configuration;

    public StaticUpdateManifestStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public UpdateManifestDto GetLatestManifest()
    {
        var section = _configuration.GetSection("Updates");

        return new UpdateManifestDto
        {
            Service = new ComponentUpdateInfo
            {
                Version = section["Service:Version"] ?? "0.0.0",
                DownloadUrl = section["Service:DownloadUrl"] ?? string.Empty,
                Sha256 = section["Service:Sha256"]
            },
            Agent = new ComponentUpdateInfo
            {
                Version = section["Agent:Version"] ?? "0.0.0",
                DownloadUrl = section["Agent:DownloadUrl"] ?? string.Empty,
                Sha256 = section["Agent:Sha256"]
            }
        };
    }
}
