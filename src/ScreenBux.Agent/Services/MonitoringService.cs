using System.Diagnostics;
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
    private readonly DispatcherTimer _timer;
    private ProcessInfo? _lastReportedProcess;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<ProcessInfo>? ProcessDetected;

    public MonitoringService()
    {
        _windowDetector = new ForegroundWindowDetector();
        _pipeClient = new NamedPipeClient();
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

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var processInfo = _windowDetector.GetForegroundProcessInfo();
            
            if (processInfo == null)
                return;

            // Only report if the process has changed
            if (_lastReportedProcess?.ProcessId == processInfo.ProcessId &&
                _lastReportedProcess?.WindowTitle == processInfo.WindowTitle)
                return;

            _lastReportedProcess = processInfo;
            ProcessDetected?.Invoke(this, processInfo);

            // Report to service
            var reportMessage = new ProcessReportMessage
            {
                Process = processInfo
            };

            var response = await _pipeClient.SendMessageAsync<object>(reportMessage);

            // Check if we received a close command
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
            }
        }
        catch (Exception ex)
        {
            RaiseStatusChanged($"Error: {ex.Message}");
        }
    }

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
                    // If graceful close fails, kill the process
                    process.Kill();
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
