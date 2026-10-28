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
            MessageBox.Show("Capture Everything is already running.\nLook for its icon in the system tray.", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _tray = new TrayIcon();
        _main = new MainWindow();

        using (var windowIcon = IconFactory.CreateAppIcon(32))
            _main.Icon = IconFactory.ToImageSource(windowIcon);

        _tray.CaptureRequested += async (_, _) => await _main.CaptureAsync();
        _tray.VideoToggleRequested += async (_, _) => await _main.ToggleVideoRecordingAsync();
        _tray.RecordToggleRequested += async (_, _) => await _main.ToggleRecordingAsync();
        _tray.OpenRequested += (_, _) => _main.ShowFromTray();
        _tray.ExitRequested += (_, _) => ExitApp();
        _main.RecordingStateChanged += (_, recording) => _tray.SetRecording(recording);
        _main.VideoRecordingStateChanged += (_, recording) => _tray.SetVideoRecording(recording);

        // Closing the window hides it to the tray instead of quitting.
        _main.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _main.Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloon("Capture Everything is still running",
                    "Click the tray icon to catch a screenshot. Right-click it for more options.");
            }
        };

        _main.Show();
    }

    private async void ExitApp()
    {
        if (_main != null && !await _main.ConfirmExitWhileRecordingAsync()) return;
        _isExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _main?.DisposeRecorder();
        _tray?.Dispose();
        if (_singleInstance != null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
