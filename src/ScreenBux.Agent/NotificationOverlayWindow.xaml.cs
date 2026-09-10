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
