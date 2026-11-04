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
    private string _usageTrayText = string.Empty;

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

        var text = string.Join(Environment.NewLine,
            new[] { _trayBaseText, _usageTrayText, _grantRemainingText }.Where(t => !string.IsNullOrEmpty(t)));

        try
        {
            // .NET (Core) WinForms allows 127 characters; fall back to the classic 63 if not.
            _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
        }
        catch (ArgumentOutOfRangeException)
        {
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }
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

        // Start hidden in the tray rather than showing the main window on launch. Hiding a
        // window that was actually shown/laid-out (rather than starting in WindowState.Minimized
        // before ever being shown) avoids the OS briefly rendering a tiny, content-less window.
        Hide();
        ShowInTaskbar = false;
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
        await CheckUsageStatusAsync();
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

    /// <summary>
    /// Asks the Service for today's usage vs. limits and renders it in the "Today" panel and the
    /// tray tooltip. Hidden entirely when the Service isn't reachable.
    /// </summary>
    private async Task CheckUsageStatusAsync()
    {
        try
        {
            var status = await _pipeClient.SendMessageAsync<ScreenBux.Shared.Messages.UsageStatusResponse>(
                new ScreenBux.Shared.Messages.UsageStatusRequest());

            if (status is null)
            {
                UsagePanel.Visibility = Visibility.Collapsed;
                _usageTrayText = string.Empty;
                return;
            }

            RenderUsageStatus(status);
        }
        catch
        {
            // Best-effort; leave the previous status on transient failure.
        }
        finally
        {
            RefreshTrayTooltip();
        }
    }

    private void RenderUsageStatus(ScreenBux.Shared.Messages.UsageStatusResponse status)
    {
        UsageItems.Children.Clear();

        if (!status.TrackUsage)
        {
            UsageItems.Children.Add(new TextBlock
            {
                Text = "Usage is not counted in this mode.",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        // Total for the day.
        if (status.DailyBudgetMinutes is int budget)
        {
            AddUsageRow("Total screen time", status.UsedSecondsToday, budget,
                $"then {status.DailyBudgetExceededAction.ToLowerInvariant()}", isBlocked: false);
            var left = Math.Max(0, budget * 60L - status.UsedSecondsToday);
            _usageTrayText = status.TrackUsage
                ? $"Used {FormatDuration(status.UsedSecondsToday)} of {FormatDuration(budget * 60L)} ({FormatDuration(left)} left)"
                : "Usage not counted";
        }
        else
        {
            UsageItems.Children.Add(new TextBlock
            {
                Text = $"Total screen time: {FormatDuration(status.UsedSecondsToday)} (no daily limit)",
                Margin = new Thickness(0, 0, 0, 4)
            });
            _usageTrayText = status.TrackUsage ? $"Used today: {FormatDuration(status.UsedSecondsToday)}" : "Usage not counted";
        }

        foreach (var category in status.Categories)
        {
            if (category.BudgetMinutes is int categoryBudget)
            {
                AddUsageRow(category.Label, category.UsedSeconds, categoryBudget, category.WindowNote, category.IsBlockedNow);
            }
            else
            {
                var state = category.IsBlockedNow ? "blocked now" : "allowed now";
                var note = string.IsNullOrEmpty(category.WindowNote) ? string.Empty : $", {category.WindowNote}";
                UsageItems.Children.Add(new TextBlock
                {
                    Text = $"{category.Label}: {state}{note}",
                    Foreground = category.IsBlockedNow ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.Black,
                    Margin = new Thickness(0, 2, 0, 4)
                });
            }
        }

        if (!string.IsNullOrEmpty(status.NextEvent))
        {
            UsageItems.Children.Add(new TextBlock
            {
                Text = $"Next: {status.NextEvent}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        UsagePanel.Visibility = UsageItems.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddUsageRow(string label, long usedSeconds, int budgetMinutes, string? note, bool isBlocked)
    {
        var budgetSeconds = Math.Max(1L, budgetMinutes * 60L);
        var exhausted = usedSeconds >= budgetSeconds;
        var left = Math.Max(0, budgetSeconds - usedSeconds);

        var header = $"{label}: {FormatDuration(usedSeconds)} of {FormatDuration(budgetSeconds)}"
                     + (exhausted ? " - time is up" : $" ({FormatDuration(left)} left)")
                     + (isBlocked && !exhausted ? " - blocked now" : string.Empty)
                     + (string.IsNullOrEmpty(note) ? string.Empty : $", {note}");

        UsageItems.Children.Add(new TextBlock { Text = header, Margin = new Thickness(0, 2, 0, 1) });

        var fraction = Math.Min(1.0, (double)usedSeconds / budgetSeconds);
        UsageItems.Children.Add(new System.Windows.Controls.ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = fraction,
            Height = 10,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = exhausted || isBlocked ? System.Windows.Media.Brushes.Firebrick : fraction >= 0.85 ? System.Windows.Media.Brushes.DarkOrange : System.Windows.Media.Brushes.SeaGreen
        });
    }

    private static string FormatDuration(long seconds)
    {
        var minutes = seconds / 60;
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
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
            ServiceStatusText.Text = isAvailable ? "Status: Connected" : "Status: Disconnected";
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
