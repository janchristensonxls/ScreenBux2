using System.Runtime.InteropServices;
using System.Diagnostics;
using ScreenBux.Shared.Models;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Service for detecting the foreground window on Windows
/// </summary>
public class ForegroundWindowDetector
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Windows API imports
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Gets information about the current foreground window
    /// </summary>
    public ProcessInfo? GetForegroundProcessInfo()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);

            if (processId == 0)
                return null;

            var process = Process.GetProcessById((int)processId);
            if (process == null)
                return null;

            var windowTitle = GetWindowTitle(hwnd);

            return new ProcessInfo
            {
                ProcessId = (int)processId,
                ProcessName = process.ProcessName,
                WindowTitle = windowTitle,
                ExecutablePath = GetProcessExecutablePath(process),
                DetectedAt = DateTime.UtcNow
            };
        }
        catch (Exception)
        {
            // Process may have exited or access denied
            return null;
        }
    }

    private string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            int length = GetWindowTextLength(hwnd);
            if (length == 0)
                return string.Empty;

            var builder = new System.Text.StringBuilder(length + 1);
            GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetProcessExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Enumerates all currently visible top-level windows with a non-empty title, one entry
    /// per window (a process with multiple windows may appear more than once). Used to enrich
    /// the Service's on-demand process list, since only the Agent - running in the interactive
    /// session - can see window state; the Service (Session 0) cannot.
    /// </summary>
    public List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd))
                {
                    return true;
                }

                var title = GetWindowTitle(hwnd);
                if (string.IsNullOrEmpty(title))
                {
                    return true;
                }

                GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == 0)
                {
                    return true;
                }

                windows.Add(new WindowInfo
                {
                    ProcessId = (int)processId,
                    WindowTitle = title
                });
            }
            catch
            {
                // Ignore this window and keep enumerating.
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
