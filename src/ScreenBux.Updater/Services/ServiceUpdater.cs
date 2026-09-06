using System.IO.Compression;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace ScreenBux.Updater.Services;

/// <summary>
/// Installs the "ScreenBux Parental Control Service" Windows Service if it isn't registered
/// yet (first run / fresh machine), or stops/replaces/restarts it if it already is (a normal
/// update). Either way this has to be driven by a separate, always-running process (the
/// Updater) because a service cannot safely delete/overwrite its own running executable, and
/// a not-yet-installed service obviously can't install itself.
/// </summary>
public class ServiceUpdater
{
    private const string ServiceName = "ScreenBux Parental Control Service";
    private const string DisplayName = "ScreenBux Parental Control Service";
    private static readonly TimeSpan ServiceControlTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<ServiceUpdater> _logger;

    public ServiceUpdater(ILogger<ServiceUpdater> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies <paramref name="updatePackagePath"/> (a downloaded zip of the published Service
    /// output) to <paramref name="installDirectory"/>. If the service is not yet registered
    /// with the SCM, this performs a first-time install (extract, then register + start) using
    /// <paramref name="executablePath"/> as the service binary; otherwise it stops the running
    /// service, replaces the files, and restarts it. Returns true if the service ends up
    /// installed and (if it was running before, or this was a fresh install) started.
    /// </summary>
    public bool ApplyUpdate(string updatePackagePath, string installDirectory, string executablePath)
    {
        return IsServiceInstalled()
            ? ApplyUpdateToExistingService(updatePackagePath, installDirectory)
            : PerformFirstInstall(updatePackagePath, installDirectory, executablePath);
    }

    /// <summary>
    /// Stops and unregisters the "ScreenBux Parental Control Service" if it is currently
    /// installed, and deletes <paramref name="installDirectory"/>. Used when uninstalling
    /// <c>ScreenBux.Updater</c> itself, since the Updater is what installed this service in the
    /// first place (via <see cref="ServiceInstaller"/>) and nothing else knows how to remove
    /// it - an ordinary MSI/uninstaller for the Updater has no idea this service exists.
    /// Safe to call even if the service was never installed (no-op, returns true).
    /// </summary>
    public bool RemoveManagedService(string? installDirectory)
    {
        if (!IsServiceInstalled())
        {
            _logger.LogInformation("{ServiceName} is not installed; nothing to remove.", ServiceName);
            return true;
        }

        try
        {
            using (var controller = new ServiceController(ServiceName))
            {
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    _logger.LogInformation("Stopping {ServiceName} for removal.", ServiceName);
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceControlTimeout);
                }
            }

            ServiceInstaller.Delete(ServiceName);
            _logger.LogInformation("{ServiceName} unregistered.", ServiceName);

            if (!string.IsNullOrEmpty(installDirectory) && Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
                _logger.LogInformation("Removed install directory {InstallDirectory}.", installDirectory);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove {ServiceName}.", ServiceName);
            return false;
        }
    }

    private bool IsServiceInstalled()
    {
        return ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase));
    }

    private bool PerformFirstInstall(string updatePackagePath, string installDirectory, string executablePath)
    {
        try
        {
            _logger.LogInformation("{ServiceName} is not installed; performing first-time install to {InstallDirectory}.", ServiceName, installDirectory);

            Directory.CreateDirectory(installDirectory);
            ZipFile.ExtractToDirectory(updatePackagePath, installDirectory, overwriteFiles: true);

            ServiceInstaller.Install(ServiceName, DisplayName, executablePath);

            using var controller = new ServiceController(ServiceName);
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, ServiceControlTimeout);

            _logger.LogInformation("{ServiceName} installed and started.", ServiceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform first-time install of {ServiceName}.", ServiceName);
            return false;
        }
    }

    private bool ApplyUpdateToExistingService(string updatePackagePath, string installDirectory)
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

/// <summary>
/// Thin P/Invoke wrapper around the Service Control Manager APIs needed to register a new
/// Windows Service. <see cref="System.ServiceProcess.ServiceController"/> can only observe and
/// control existing services; it has no API to create one, so this fills that gap.
/// </summary>
internal static class ServiceInstaller
{
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;

    public static void Install(string serviceName, string displayName, string binaryPath)
    {
        using var scmHandle = OpenSCManager(null, null, SC_MANAGER_CREATE_SERVICE);
        if (scmHandle.IsInvalid)
        {
            throw new InvalidOperationException($"OpenSCManager failed (Win32Error={Marshal.GetLastWin32Error()}).");
        }

        using var serviceHandle = CreateService(
            scmHandle,
            serviceName,
            displayName,
            SERVICE_ALL_ACCESS,
            SERVICE_WIN32_OWN_PROCESS,
            SERVICE_AUTO_START,
            SERVICE_ERROR_NORMAL,
            $"\"{binaryPath}\"",
            null,
            IntPtr.Zero,
            null,
            null,
            null);

        if (serviceHandle.IsInvalid)
        {
            throw new InvalidOperationException($"CreateService failed for '{serviceName}' (Win32Error={Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>
    /// Unregisters a service previously created with <see cref="Install"/>. Safe to call even
    /// if the service does not exist (SCM just returns "does not exist", which is treated as
    /// success here since the end state - "no such service" - is already achieved).
    /// </summary>
    public static void Delete(string serviceName)
    {
        const uint SC_MANAGER_CONNECT = 0x0001;
        const uint SERVICE_DELETE = 0x00010000;
        const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

        using var scmHandle = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scmHandle.IsInvalid)
        {
            throw new InvalidOperationException($"OpenSCManager failed (Win32Error={Marshal.GetLastWin32Error()}).");
        }

        using var serviceHandle = OpenService(scmHandle, serviceName, SERVICE_DELETE);
        if (serviceHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_SERVICE_DOES_NOT_EXIST)
            {
                return;
            }

            throw new InvalidOperationException($"OpenService failed for '{serviceName}' (Win32Error={error}).");
        }

        if (!DeleteService(serviceHandle))
        {
            throw new InvalidOperationException($"DeleteService failed for '{serviceName}' (Win32Error={Marshal.GetLastWin32Error()}).");
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle hSCManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(SafeServiceHandle hService);

    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle CreateService(
        SafeServiceHandle hSCManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    private sealed class SafeServiceHandle : SafeHandle
    {
        public SafeServiceHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
}
