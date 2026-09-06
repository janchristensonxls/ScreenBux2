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
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

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
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;

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

        InitializeNotifyIcon();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Sets up the system tray ("notification area") icon and its context menu.
    /// </summary>
    private void InitializeNotifyIcon()
    {
        var icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location)
                   ?? Drawing.SystemIcons.Application;

        var contextMenu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("Show", null, (_, _) => ShowMainWindow());
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "ScreenBux Agent",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // Minimize to the tray instead of exiting when the user closes the window.
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
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

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private async void ServiceStatusTimer_Tick(object? sender, EventArgs e)
    {
        await CheckServiceStatusAsync();
        await CheckGrantStatusAsync();
    }

    private async Task CheckGrantStatusAsync()
    {
        try
        {
            var response = await _pipeClient.SendMessageAsync<ScreenBux.Shared.Messages.GrantStatusResponse>(
                new ScreenBux.Shared.Messages.GrantStatusRequest());

            if (response?.ExpiresAtUtc is DateTime expiresAtUtc && expiresAtUtc > DateTime.UtcNow)
            {
                var remaining = expiresAtUtc - DateTime.UtcNow;
                GrantStatusText.Text = $"Bonus time active: {remaining:hh\\:mm\\:ss} remaining";
            }
            else
            {
                GrantStatusText.Text = string.Empty;
            }
        }
        catch
        {
            // Best-effort; leave the previous text on transient failure.
        }
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
            ServiceStatusText.Foreground = isAvailable ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;

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
        //todo! Just for testing, we will always show the link panel until we have a proper UI for linked devices.
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
