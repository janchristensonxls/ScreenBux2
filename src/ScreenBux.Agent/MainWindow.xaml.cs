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
    private Forms.ToolStripMenuItem? _linkDeviceMenuItem;
    private Drawing.Icon? _connectedIcon;
    private Drawing.Icon? _disconnectedIcon;
    private bool? _lastKnownConnected;
    private string _trayBaseText = "ScreenBux Agent";
    private string _grantRemainingText = string.Empty;

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
        _connectedIcon = LoadIconResource("Assets/tray-connected.ico");
        _disconnectedIcon = LoadIconResource("Assets/tray-disconnected.ico");

        var fallbackIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location)
                            ?? Drawing.SystemIcons.Application;

        var contextMenu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("Show", null, (_, _) => ShowMainWindow());
        _linkDeviceMenuItem = new Forms.ToolStripMenuItem("Link Device...", null, (_, _) => OpenLinkDeviceWindow())
        {
            Visible = false
        };
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_linkDeviceMenuItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _disconnectedIcon ?? fallbackIcon,
            Text = "ScreenBux Agent",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>
    /// Loads an .ico file that was added to the project as a WPF Resource (pack URI),
    /// e.g. Assets/tray-connected.ico. Returns null if the resource is missing so the
    /// caller can fall back to a default icon instead of crashing.
    /// </summary>
    private static Drawing.Icon? LoadIconResource(string relativePath)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo is null)
            {
                return null;
            }

            using var stream = streamInfo.Stream;
            return new Drawing.Icon(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Swaps the tray icon to reflect whether the ScreenBux Service is currently reachable.
    /// </summary>
    private void UpdateTrayConnectionState(bool isConnected)
    {
        _trayBaseText = isConnected ? "ScreenBux Agent - Connected" : "ScreenBux Agent - Disconnected";

        if (_notifyIcon is null || _lastKnownConnected == isConnected)
        {
            RefreshTrayTooltip();
            return;
        }

        _lastKnownConnected = isConnected;
        var icon = isConnected ? _connectedIcon : _disconnectedIcon;
        if (icon is not null)
        {
            _notifyIcon.Icon = icon;
        }

        RefreshTrayTooltip();
    }

    /// <summary>
    /// Rebuilds the tray icon's tooltip from the connection state plus any remaining bonus
    /// time, truncating to NotifyIcon.Text's 63-character limit.
    /// </summary>
    private void RefreshTrayTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var text = string.IsNullOrEmpty(_grantRemainingText)
            ? _trayBaseText
            : $"{_trayBaseText}{Environment.NewLine}{_grantRemainingText}";

        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void OpenLinkDeviceWindow()
    {
        var linkWindow = new LinkDeviceWindow(_pipeClient)
        {
            Owner = this
        };
        linkWindow.ShowDialog();
        if (linkWindow.LinkSucceeded)
        {
            LogMessage("Device linked successfully.");
            RefreshLinkedState();
        }
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
        // The agent is always supposed to be running; closing the window just minimizes it to the tray.
        // A watchdog in the service restarts the process if it is ever actually terminated.
        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshLinkedState();

        // Check service status
        await CheckServiceStatusAsync();
        _serviceStatusTimer.Start();

        _monitoringService.Start();
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

        _connectedIcon?.Dispose();
        _disconnectedIcon?.Dispose();
    }

    private async void ServiceStatusTimer_Tick(object? sender, EventArgs e)
    {
        await CheckServiceStatusAsync();
        await CheckGrantStatusAsync();
        await CheckPolicyNameAsync();
    }

    private async Task CheckPolicyNameAsync()
    {
        try
        {
            var response = await _pipeClient.SendMessageAsync<ScreenBux.Shared.Messages.PolicyResponse>(
                new ScreenBux.Shared.Messages.GetPolicyRequest());

            var name = response?.Configuration?.Name;
            PolicyNameText.Text = string.IsNullOrWhiteSpace(name) ? string.Empty : $"Policy: {name}";
        }
        catch
        {
            // Best-effort; leave the previous text on transient failure.
        }
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
                _grantRemainingText = $"Bonus time: {remaining:hh\\:mm\\:ss} remaining";
            }
            else
            {
                GrantStatusText.Text = string.Empty;
                _grantRemainingText = string.Empty;
            }
        }
        catch
        {
            // Best-effort; leave the previous text on transient failure.
        }
        finally
        {
            RefreshTrayTooltip();
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
            UpdateTrayConnectionState(isAvailable);

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

    /// <summary>
    /// Shows the "not linked" info block in place of the activity log, and toggles the
    /// "Link Device..." tray menu item, based on whether this device is linked to a parent account.
    /// </summary>
    private void RefreshLinkedState()
    {
        var linked = PolicyStorage.IsDeviceLinked();
        NotLinkedPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
        LogPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
        if (_linkDeviceMenuItem is not null)
        {
            _linkDeviceMenuItem.Visible = !linked;
        }

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
