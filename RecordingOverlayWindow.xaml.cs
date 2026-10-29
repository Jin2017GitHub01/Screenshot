using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static Screenshot.NativeMethods;

namespace Screenshot;

/// <summary>
/// Floating recording pill (timer + audio meter + Stop button) and a click-through
/// border frame around the recorded screen region. Both windows use
/// <see cref="NativeMethods.WDA_EXCLUDEFROMCAPTURE"/> so they never appear in the video.
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    private const int FrameThicknessPx = 2;
    private const int PillGapPx = 10;

    private readonly Int32Rect _region;
    private readonly Window _frameWindow;

    public event EventHandler? StopRequested;

    public RecordingOverlayWindow(Int32Rect region)
    {
        InitializeComponent();
        _region = region;
        _frameWindow = CreateFrameWindow(region);
        ApplyLanguage();
        L10n.LanguageChanged += OnLanguageChanged;

        SourceInitialized += OnPillSourceInitialized;
        Loaded += (_, _) =>
        {
            PositionPill();
            RecordDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        };
        Closed += (_, _) =>
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            RecordDot.BeginAnimation(OpacityProperty, null);
            _frameWindow.Close();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        PillBorder.ToolTip = L10n.OverlayDragTip;
        LevelMeter.ToolTip = L10n.OverlayAudioMeterTip;
        StopButton.ToolTip = L10n.StopVideoTip;
        StopText.Text = L10n.StopPrefix;
    }

    public void ShowOverlay()
    {
        _frameWindow.Show();
        Show();
    }

    public void UpdateStatus(TimeSpan elapsed, double level)
    {
        TimerText.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
        LevelFill.Width = LevelMeter.Width * Math.Clamp(level, 0, 1);
    }

    private void OnPillSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    private void PositionPill()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        GetWindowRect(hwnd, out RECT pillRect);
        int pillW = Math.Max(200, pillRect.Right - pillRect.Left);
        int pillH = Math.Max(48, pillRect.Bottom - pillRect.Top);

        var area = new RECT
        {
            Left = _region.X,
            Top = _region.Y,
            Right = _region.X + _region.Width,
            Bottom = _region.Y + _region.Height
        };
        var (_, work) = GetMonitorRects(area);

        int x = _region.X + (_region.Width - pillW) / 2;
        x = Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - pillW - 8));

        int y;
        if (area.Bottom + PillGapPx + pillH <= work.Bottom)
        {
            // Prefer just below the recorded region.
            y = area.Bottom + PillGapPx;
        }
        else if (area.Top - PillGapPx - pillH >= work.Top)
        {
            // Otherwise place just above the recorded region.
            y = area.Top - PillGapPx - pillH;
        }
        else
        {
            // Fullscreen / large region: tuck near the top-center of the monitor work area.
            y = work.Top + 12;
        }

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) =>
        StopRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Creates the click-through border outline around the recorded region.</summary>
    private static Window CreateFrameWindow(Int32Rect region)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(FrameThicknessPx),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xEF, 0x44, 0x44)),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        var win = new Window
        {
            Title = "Recording Region Frame",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 100,
            Height = 100,
            Content = border
        };

        int frameX = region.X - FrameThicknessPx;
        int frameY = region.Y - FrameThicknessPx;
        int frameW = region.Width + FrameThicknessPx * 2;
        int frameH = region.Height + FrameThicknessPx * 2;

        win.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(FrameWndProc);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            SetWindowPos(hwnd, HWND_TOPMOST, frameX, frameY, frameW, frameH, SWP_NOACTIVATE);
        };
        win.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, frameX, frameY, frameW, frameH, SWP_NOACTIVATE);
        };

        return win;
    }

    private static IntPtr FrameWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED) handled = true;
        return IntPtr.Zero;
    }
}

