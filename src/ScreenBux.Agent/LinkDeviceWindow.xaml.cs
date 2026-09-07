using System.Windows;
using System.Windows.Media;
using ScreenBux.Agent.Services;

namespace ScreenBux.Agent;

/// <summary>
/// Interaction logic for LinkDeviceWindow.xaml
/// </summary>
public partial class LinkDeviceWindow : Window
{
    private readonly NamedPipeClient _pipeClient;

    /// <summary>
    /// True when the device was successfully linked before the window was closed.
    /// </summary>
    public bool LinkSucceeded { get; private set; }

    public LinkDeviceWindow(NamedPipeClient pipeClient)
    {
        InitializeComponent();
        _pipeClient = pipeClient;
    }

    private async void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        var code = LinkCodeTextBox.Text.Trim().ToUpperInvariant();
        if (code.Length != 8)
        {
            SetStatus("Error: Link code must be exactly 8 characters.", isError: true);
            return;
        }

        LinkButton.IsEnabled = false;
        SetStatus("Linking device...", isError: false);

        try
        {
            var request = new ScreenBux.Shared.Messages.LinkDeviceRequest { LinkCode = code };
            var response = await _pipeClient.SendMessageAsync<ScreenBux.Shared.Messages.LinkDeviceResponse>(request);

            if (response is null)
            {
                SetStatus("Error: Service did not respond. Ensure the ScreenBux Service is running.", isError: true);
            }
            else if (response.Success)
            {
                LinkSucceeded = true;
                SetStatus("Device linked successfully!", isError: false);
                Close();
            }
            else
            {
                SetStatus($"Link failed: {response.Message}", isError: true);
            }
        }
        finally
        {
            LinkButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.DarkGreen;
    }
}
