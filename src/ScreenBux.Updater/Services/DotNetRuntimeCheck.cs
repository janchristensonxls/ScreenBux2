namespace ScreenBux.Updater.Services;

/// <summary>
/// Detects installed shared .NET frameworks by looking in the standard x64 install location
/// (%ProgramFiles%\dotnet\shared\&lt;framework&gt;\&lt;version&gt;).
/// </summary>
internal static class DotNetRuntimeCheck
{
    public static bool IsDesktopRuntimeInstalled(int majorVersion) =>
        IsFrameworkInstalled("Microsoft.WindowsDesktop.App", majorVersion);

    public static bool IsFrameworkInstalled(string frameworkName, int majorVersion)
    {
        try
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnetRoot))
            {
                dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            }

            var frameworkDir = Path.Combine(dotnetRoot, "shared", frameworkName);
            return Directory.Exists(frameworkDir)
                && Directory.EnumerateDirectories(frameworkDir, $"{majorVersion}.*").Any();
        }
        catch
        {
            // Can't tell - don't raise a false alarm.
            return true;
        }
    }
}
