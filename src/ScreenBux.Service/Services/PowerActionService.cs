using System.Runtime.InteropServices;

namespace ScreenBux.Service.Services;

/// <summary>
/// Executes device-wide power actions (Sleep/Hibernate) requested by an "Always" policy rule
/// (e.g. a strict "Sleep" lockout mode). Uses the Win32 <c>SetSuspendState</c> API directly
/// rather than <see cref="System.Windows.Forms.Application"/>/PowerShell, since the Service
/// has no UI dependency.
/// </summary>
public class PowerActionService
{
    private readonly ILogger<PowerActionService> _logger;

    public PowerActionService(ILogger<PowerActionService> logger)
    {
        _logger = logger;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    /// <summary>
    /// Puts the device to sleep. Returns true if the call succeeded (the process typically
    /// suspends immediately after, so callers should not assume further code runs promptly).
    /// </summary>
    public bool Sleep()
    {
        _logger.LogWarning("Executing Sleep power action");
        var result = SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
        if (!result)
        {
            _logger.LogError("SetSuspendState(Sleep) failed. Win32Error={Error}", Marshal.GetLastWin32Error());
        }

        return result;
    }

    /// <summary>
    /// Hibernates the device. Falls back to <see cref="Sleep"/> if hibernation fails or is
    /// unavailable (commonly disabled/unsupported), since Sleep is the safe default behavior.
    /// </summary>
    public bool Hibernate()
    {
        _logger.LogWarning("Executing Hibernate power action");
        var result = SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false);
        if (!result)
        {
            _logger.LogWarning("SetSuspendState(Hibernate) failed (Win32Error={Error}); falling back to Sleep", Marshal.GetLastWin32Error());
            return Sleep();
        }

        return result;
    }
}
