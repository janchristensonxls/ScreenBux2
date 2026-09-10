using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenBux.Shared.Messages;
using ScreenBux.Shared.Models;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Service that monitors foreground windows and reports to the Windows Service
/// </summary>
public class MonitoringService
{
    private readonly ForegroundWindowDetector _windowDetector;
    private readonly NamedPipeClient _pipeClient;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly DispatcherTimer _timer;
    private ProcessInfo? _lastReportedProcess;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<ProcessInfo>? ProcessDetected;

    public MonitoringService()
    {
        _windowDetector = new ForegroundWindowDetector();
        _pipeClient = new NamedPipeClient();
        _screenCaptureService = new ScreenCaptureService();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        _timer.Start();
        RaiseStatusChanged("Monitoring started");
    }

    public void Stop()
    {
        _timer.Stop();
        RaiseStatusChanged("Monitoring stopped");
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>
    /// True only while this Agent's own session is the active console session. GetForegroundWindow
    /// is scoped to the caller's session, not to whichever session is physically displayed, so an
    /// Agent left running in a disconnected/switched-away session (fast user switching) would
    /// otherwise keep reporting its last-focused window as if it were still being actively used,
    /// double-counting/corrupting usage tracking for a session nobody is looking at.
    /// </summary>
    private static bool IsRunningInActiveConsoleSession()
    {
        try
        {
            return WTSGetActiveConsoleSessionId() == (uint)Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            // If we can't tell, err on the side of reporting rather than going silent.
            return true;
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (!IsRunningInActiveConsoleSession())
            {
                return;
            }

            var processInfo = _windowDetector.GetForegroundProcessInfo();

            if (processInfo == null)
                return;

            // Only raise the detected-change event/notify listeners if the process has changed,
            // but still send the report every tick below - the response to this round-trip is
            // also how the Service piggybacks an on-demand "get window list" request (polling
            // model), so we can't skip the pipe call just because the window is unchanged.
            var isNewDetection = _lastReportedProcess?.ProcessId != processInfo.ProcessId ||
                _lastReportedProcess?.WindowTitle != processInfo.WindowTitle;

            if (isNewDetection)
            {
                _lastReportedProcess = processInfo;
                ProcessDetected?.Invoke(this, processInfo);
            }

            // Report to service
            var reportMessage = new ProcessReportMessage
            {
                Process = processInfo
            };

            var response = await _pipeClient.SendMessageForPolymorphicResponseAsync(reportMessage);

            // A close command means the Service's own (elevated) enforcement attempt failed -
            // this is a fallback ask for a best-effort graceful/local close, not the primary
            // enforcement path.
            if (response is CloseProcessCommand closeCommand)
            {
                RaiseStatusChanged($"Received close command for PID {closeCommand.ProcessId}: {closeCommand.Reason}");
                await TryCloseProcessAsync(closeCommand.ProcessId);
            }
            else if (response is CommandResponse cmdResponse)
            {
                if (!cmdResponse.Success)
                {
                    RaiseStatusChanged($"Service response: {cmdResponse.Message}");
                }

                if (cmdResponse.PendingWindowListRequestId is Guid requestId)
                {
                    await ReportWindowListAsync(requestId);
                }

                if (cmdResponse.PendingScreenCaptureRequestId is Guid captureRequestId)
                {
                    await ReportScreenCaptureAsync(captureRequestId);
                }
            }
        }
        catch (Exception ex)
        {
            RaiseStatusChanged($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Enumerates currently visible windows and reports them back to the Service for a
    /// pending on-demand "get process list" request. Sent as its own transactional pipe
    /// call (rather than waiting for the next 2s tick) so the parent doesn't wait longer
    /// than necessary for the result.
    /// </summary>
    private async Task ReportWindowListAsync(Guid requestId)
    {
        try
        {
            var windows = _windowDetector.GetVisibleWindows();
            var report = new WindowListReportMessage
            {
                RequestId = requestId,
                Windows = windows
            };

            await _pipeClient.SendMessageAsync<object>(report);
        }
        catch (Exception ex)
        {
            RaiseStatusChanged($"Error reporting window list: {ex.Message}");
        }
    }

    /// <summary>
    /// Captures all connected monitors and reports the JPEG images back to the Service for a
    /// pending on-demand "screen capture" request. Sent as its own transactional pipe call
    /// (rather than waiting for the next tick) so the parent doesn't wait longer than necessary.
    /// </summary>
    private async Task ReportScreenCaptureAsync(Guid requestId)
    {
        try
        {
            var images = _screenCaptureService.CaptureAllScreens();
            var report = new ScreenCaptureReportMessage
            {
                RequestId = requestId,
                Success = true,
                Images = images
            };

            await _pipeClient.SendMessageAsync<object>(report);
        }
        catch (Exception ex)
        {
            RaiseStatusChanged($"Error capturing screens: {ex.Message}");

            try
            {
                await _pipeClient.SendMessageAsync<object>(new ScreenCaptureReportMessage
                {
                    RequestId = requestId,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
            catch
            {
                // Best effort - if this fails too, the Service will time out waiting.
            }
        }
    }

    /// <summary>
    /// Fallback path used only when the Service's own (elevated) enforcement attempt fails
    /// (e.g. access denied on a protected/admin-launched process). This performs a
    /// best-effort graceful close in the Agent's own user-session token; CloseMainWindow()
    /// doesn't require elevation, but a forceful Kill() here is subject to the same rights
    /// limitations the Service already tried and failed with.
    /// </summary>
    private async Task TryCloseProcessAsync(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process != null && !process.HasExited)
            {
                // Try to close gracefully
                if (process.CloseMainWindow())
                {
                    RaiseStatusChanged($"Closed process {processId} gracefully");
                }
                else
                {
                    // If graceful close fails, kill the whole process tree (parent + all
                    // descendants) rather than just this PID - important for multi-process
                    // apps like Chromium-based browsers, where the window-owning process may
                    // otherwise leave child/helper processes running.
                    process.Kill(entireProcessTree: true);
                    RaiseStatusChanged($"Killed process {processId}");
                }
            }
        }
        catch (Exception ex)
        {
            RaiseStatusChanged($"Error closing process {processId}: {ex.Message}");
        }
    }

    private void RaiseStatusChanged(string status)
    {
        StatusChanged?.Invoke(this, status);
    }
}
