namespace ScreenBux.Shared.Utilities;

/// <summary>
/// Resolves the path to the "installed version" marker files that <c>ScreenBux.Updater</c>
/// writes after successfully applying a Service/Agent update (see
/// <c>UpdateCheckService.WriteInstalledVersion</c>), and reads them back. This is the version
/// the auto-updater actually compares against the WebServer's manifest - not necessarily the
/// same as the currently *running* process's assembly version, which can lag behind (or, in a
/// dev "dotnet run" scenario, never match at all) if the marker file is stale or missing.
///
/// The Updater's own configuration (<c>Service:InstalledVersionFile</c> / <c>Agent:InstalledVersionFile</c>)
/// is the source of truth for the path, but defaults to the same location returned here
/// (<c>%CommonApplicationData%\ScreenBux\service.version</c> / <c>agent.version</c>) - see
/// appsettings.json in ScreenBux.Updater - so callers without access to that configuration
/// (the Service and Agent processes themselves) can still find the file.
/// </summary>
public static class VersionFileStorage
{
    public static string GetServiceVersionFilePath()
    {
        return Path.Combine(GetBaseDirectory(), "service.version");
    }

    public static string GetAgentVersionFilePath()
    {
        return Path.Combine(GetBaseDirectory(), "agent.version");
    }

    private static string GetBaseDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "ScreenBux");
    }

    /// <summary>
    /// Reads the version recorded at <paramref name="path"/>, or null if the file doesn't
    /// exist or doesn't contain a parseable <see cref="Version"/>.
    /// </summary>
    public static Version? TryReadVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Version.TryParse(File.ReadAllText(path).Trim(), out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }
}
