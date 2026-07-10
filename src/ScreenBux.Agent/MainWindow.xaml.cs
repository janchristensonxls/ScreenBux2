using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScreenBux.Agent.Services;
using ScreenBux.Shared.Models;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Agent;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MonitoringService _monitoringService;
    private readonly NamedPipeClient _pipeClient;
    private readonly DispatcherTimer _serviceStatusTimer;
    private bool _isCheckingService;

    public MainWindow()
    {
        InitializeComponent();
        _monitoringService = new MonitoringService();
        _pipeClient = new NamedPipeClient();
        _serviceStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _serviceStatusTimer.Tick += ServiceStatusTimer_Tick;

        _monitoringService.StatusChanged += OnStatusChanged;
        _monitoringService.ProcessDetected += OnProcessDetected;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshLinkPanel();

        // Check service status
        await CheckServiceStatusAsync();
        _serviceStatusTimer.Start();

        _monitoringService.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Monitoring active";
        LogMessage("Monitoring started automatically");
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _serviceStatusTimer.Stop();
        _monitoringService.Stop();
    }

    private async void ServiceStatusTimer_Tick(object? sender, EventArgs e)
    {
        await CheckServiceStatusAsync();
    }

    private async Task CheckServiceStatusAsync()
    {
        if (_isCheckingService)
        {
            return;
        }

        _isCheckingService = true;
        try
        {
            var isAvailable = await _pipeClient.IsServiceAvailableAsync();
            ServiceStatusText.Text = isAvailable ? "Service: Connected" : "Service: Disconnected";
            ServiceStatusText.Foreground = isAvailable ? Brushes.Green : Brushes.Red;

            if (!isAvailable)
            {
                LogMessage("Warning: Service is not running. Please start the ScreenBux Service.");
            }
        }
        finally
        {
            _isCheckingService = false;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _monitoringService.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Monitoring active";
        LogMessage("Monitoring started");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _monitoringService.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "Monitoring stopped";
        LogMessage("Monitoring stopped");
    }

    private async void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        var code = LinkCodeTextBox.Text.Trim().ToUpperInvariant();
        if (code.Length != 8)
        {
            LogMessage("Error: Link code must be exactly 8 characters.");
            return;
        }

        LinkButton.IsEnabled = false;
        StatusText.Text = "Linking device...";
        LogMessage($"Sending link code to service: {code}");

        try
        {
            var request = new ScreenBux.Shared.Messages.LinkDeviceRequest { LinkCode = code };
            var response = await _pipeClient.SendMessageAsync<ScreenBux.Shared.Messages.LinkDeviceResponse>(request);

            if (response is null)
            {
                LogMessage("Error: Service did not respond. Ensure the ScreenBux Service is running.");
                StatusText.Text = "Link failed — service unavailable";
            }
            else if (response.Success)
            {
                LogMessage($"Device linked successfully! Device ID: {response.DeviceId}");
                StatusText.Text = "Device linked";
                LinkCodeTextBox.Clear();
                RefreshLinkPanel();
            }
            else
            {
                LogMessage($"Link failed: {response.Message}");
                StatusText.Text = "Link failed";
            }
        }
        finally
        {
            LinkButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shows the link panel only when this device is not yet linked to a parent account.
    /// </summary>
    private void RefreshLinkPanel()
    {
        var linked = PolicyStorage.IsDeviceLinked();
        LinkDevicePanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        if (linked)
        {
            LogMessage("Device is linked to a parent account.");
        }
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() =>
        {
            LogMessage($"Status: {status}");
        });
    }

    private void OnProcessDetected(object? sender, ProcessInfo process)
    {
        Dispatcher.Invoke(() =>
        {
            LogMessage($"Detected: {process.ProcessName} (PID: {process.ProcessId}) - {process.WindowTitle}");
        });
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{timestamp}] {message}\n");
        LogTextBox.ScrollToEnd();
    }
}
