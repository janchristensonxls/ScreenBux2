using System.Configuration;
using System.Data;
using System.Windows;

namespace ScreenBux.Agent;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The Agent is a tray-only app: its main window is only ever shown on demand (via the
        // tray icon's "Show" menu item). Setting WindowState="Minimized" in XAML and relying on
        // StartupUri to Show() it caused a tiny, content-less window to briefly render at the
        // bottom-left of the desktop on some systems, because the window was never actually
        // laid out/painted as a normal window before being minimized. Instead, show the window
        // normally so it initializes correctly, then hide it (rather than minimize it) once
        // MainWindow_Loaded has finished its one-time startup work.
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

