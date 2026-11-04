using System.Windows;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Agent;

/// <summary>
/// A best-effort, topmost overlay window shown for a <see cref="PendingNotification"/>. Runs in
/// the interactive user session (unlike the Service, which may run in Session 0), so it can
/// safely paint UI. Auto-dismisses after <see cref="PendingNotification.AutoDismissSeconds"/> if
/// set; otherwise stays until the user clicks "OK" (used for TimeUp notices).
/// </summary>
public partial class NotificationOverlayWindow : Window
{
    public NotificationOverlayWindow(PendingNotification notification)
    {
        InitializeComponent();

        TitleText.Text = notification.Title;
        MessageText.Text = notification.Message;
        TimeText.Text = $"{notification.CreatedAtUtc.ToLocalTime():HH:mm} · see all messages in the ScreenBux Agent";

        if (notification.Severity == NotificationSeverity.ParentMessage)
        {
            // Parent messages stand out from the Service's own (dark grey) time warnings.
            RootBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF0, 0x1B, 0x4F, 0x8C));
            MessageText.FontSize = 16;
            MessageText.Foreground = System.Windows.Media.Brushes.White;
        }

        // Bottom-right corner of the primary screen, clear of the taskbar.
        var workArea = SystemParameters.WorkArea;
        Loaded += (_, _) =>
        {
            Left = workArea.Right - ActualWidth - 24;
            Top = workArea.Bottom - ActualHeight - 24;
        };

        if (notification.AutoDismissSeconds is int seconds)
        {
            DismissButton.Visibility = Visibility.Collapsed;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
        }
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
