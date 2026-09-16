using System.Runtime.InteropServices;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Measures how long the interactive session has had no keyboard/mouse input, via the Win32
/// GetLastInputInfo API. Like <see cref="ForegroundWindowDetector"/>, this is scoped to the
/// Agent's own session - GetLastInputInfo reports the last input seen by the calling session's
/// raw input queue, not the machine as a whole.
/// </summary>
public class IdleInputDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// Returns how long it has been since the last keyboard/mouse input, or <see cref="TimeSpan.Zero"/>
    /// if the underlying API call fails. Both GetLastInputInfo's dwTime and Environment.TickCount
    /// are 32-bit millisecond counts since boot that wrap around every ~49.7 days; subtracting them
    /// as unsigned values gives the correct elapsed duration across that wraparound.
    /// </summary>
    public TimeSpan GetIdleTime()
    {
        var lastInputInfo = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lastInputInfo))
        {
            return TimeSpan.Zero;
        }

        var idleMilliseconds = (uint)Environment.TickCount - lastInputInfo.dwTime;
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }
}
