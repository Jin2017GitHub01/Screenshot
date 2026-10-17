using System.Windows;

namespace Screenshot;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\SnipByJin.SingleInstance";

    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private bool _isExiting;
    private bool _trayHintShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one instance (one tray icon) at a time.
        _singleInstance = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show("Snip is already running.\nLook for its icon in the system tray.", "Snip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _tray = new TrayIcon();
        _main = new MainWindow();

        using (var windowIcon = IconFactory.CreateAppIcon(32))
            _main.Icon = IconFactory.ToImageSource(windowIcon);

        _tray.CaptureRequested += async (_, _) => await _main.CaptureAsync();
        _tray.OpenRequested += (_, _) => _main.ShowFromTray();
        _tray.ExitRequested += (_, _) => ExitApp();

        // Closing the window hides it to the tray instead of quitting.
        _main.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _main.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloon("Snip is still running",
                    "Click the tray icon to capture. Right-click it for more options.");
            }
        };

        _main.Show();
    }

    private void ExitApp()
    {
        _isExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_singleInstance != null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
