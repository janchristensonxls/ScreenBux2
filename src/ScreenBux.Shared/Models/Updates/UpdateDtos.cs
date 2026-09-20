namespace ScreenBux.Shared.Models.Updates;

/// <summary>
/// Describes the latest available build of a single deployable component (the Windows
/// Service or the Agent). <see cref="Version"/> is compared against the locally recorded
/// installed version to decide whether an update is needed.
/// </summary>
public class ComponentUpdateInfo
{
    /// <summary>Version string, e.g. "1.4.2" (parsed with <see cref="System.Version"/>).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>HTTPS URL to a .zip containing the published output for this component.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional SHA-256 (hex, case-insensitive) of the zip. When present, the Updater verifies
    /// the downloaded package against it before installing and rejects a mismatch; when absent,
    /// the integrity check is skipped (logged as a warning).
    /// </summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// The full update manifest returned by <c>GET api/updates/latest</c>: the latest known
/// version of each independently-updatable component.
/// </summary>
public class UpdateManifestDto
{
    public ComponentUpdateInfo Service { get; set; } = new();

    public ComponentUpdateInfo Agent { get; set; } = new();
}
