using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenBux.Shared.Models;

namespace ScreenBux.Service.Services;

/// <summary>
/// Service for detecting the foreground window on Windows
/// </summary>
public class ForegroundWindowDetector
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public ProcessInfo? GetForegroundProcessInfo()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);

            if (processId == 0)
            {
                return null;
            }

            var process = Process.GetProcessById((int)processId);
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
            return null;
        }
    }

    private string GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            int length = GetWindowTextLength(hwnd);
            if (length == 0)
            {
                return string.Empty;
            }

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
}
