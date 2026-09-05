using System.IO.Compression;
using System.ServiceProcess;

namespace ScreenBux.Updater.Services;

/// <summary>
/// Stops the "ScreenBux Parental Control Service" Windows Service, replaces its installed
/// files with the contents of a downloaded update package, and starts it back up. This has to
/// be driven by a separate, always-running process (the Updater) because a service cannot
/// safely delete/overwrite its own running executable.
/// </summary>
public class ServiceUpdater
{
    private const string ServiceName = "ScreenBux Parental Control Service";
    private static readonly TimeSpan ServiceControlTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ServiceUpdater> _logger;

    public ServiceUpdater(ILogger<ServiceUpdater> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies <paramref name="updatePackagePath"/> (a downloaded zip of the published Service
    /// output) to <paramref name="installDirectory"/>, stopping and restarting the Windows
    /// Service around the file replacement. Returns true if the service was successfully
    /// restarted afterward.
    /// </summary>
    public bool ApplyUpdate(string updatePackagePath, string installDirectory)
    {
        using var controller = new ServiceController(ServiceName);

        var wasRunning = false;
        try
        {
            controller.Refresh();
            wasRunning = controller.Status != ServiceControllerStatus.Stopped;

            if (wasRunning)
            {
                _logger.LogInformation("Stopping {ServiceName} for update.", ServiceName);
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceControlTimeout);
            }

            _logger.LogInformation("Extracting update package {Package} to {InstallDirectory}.", updatePackagePath, installDirectory);
            ZipFile.ExtractToDirectory(updatePackagePath, installDirectory, overwriteFiles: true);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update for {ServiceName}.", ServiceName);
            return false;
        }
        finally
        {
            if (wasRunning)
            {
                try
                {
                    controller.Refresh();
                    if (controller.Status != ServiceControllerStatus.Running)
                    {
                        _logger.LogInformation("Starting {ServiceName} after update.", ServiceName);
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, ServiceControlTimeout);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart {ServiceName} after update.", ServiceName);
                }
            }
        }
    }
}
