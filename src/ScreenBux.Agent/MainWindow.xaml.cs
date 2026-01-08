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
using ScreenBux.Agent.Services;
using ScreenBux.Shared.Models;

namespace ScreenBux.Agent;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MonitoringService _monitoringService;
    private readonly NamedPipeClient _pipeClient;

    public MainWindow()
    {
        InitializeComponent();
        _monitoringService = new MonitoringService();
        _pipeClient = new NamedPipeClient();

        _monitoringService.StatusChanged += OnStatusChanged;
        _monitoringService.ProcessDetected += OnProcessDetected;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Check service status
        await CheckServiceStatusAsync();
    }

    private async Task CheckServiceStatusAsync()
    {
        var isAvailable = await _pipeClient.IsServiceAvailableAsync();
        ServiceStatusText.Text = isAvailable ? "Service: Connected" : "Service: Disconnected";
        ServiceStatusText.Foreground = isAvailable ? Brushes.Green : Brushes.Red;
        
        if (!isAvailable)
        {
            LogMessage("Warning: Service is not running. Please start the ScreenBux Service.");
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
