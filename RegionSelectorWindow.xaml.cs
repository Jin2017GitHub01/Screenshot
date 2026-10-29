using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Screenshot;

/// <summary>
/// Borderless window covering the whole virtual desktop (all monitors) that shows a frozen
/// snapshot of the screen and lets the user drag a rectangle to select a region.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private const int MinSelectionPx = 3;

    private readonly BitmapSource _screen;
    private readonly Int32Rect _bounds;
    private readonly bool _forVideo;
    private readonly RectangleGeometry _fullGeometry = new();
    private readonly RectangleGeometry _holeGeometry = new(new Rect(0, 0, 0, 0));
    private Point? _dragStart;

    /// <summary>Selected region in pixel coordinates of the snapshot bitmap.</summary>
    public Int32Rect SelectedRegion { get; private set; }

    public RegionSelectorWindow(BitmapSource screen, Int32Rect virtualBounds, bool forVideo = false)
    {
        InitializeComponent();
        _screen = screen;
        _bounds = virtualBounds;
        _forVideo = forVideo;

        if (forVideo)
        {
            HintIcon.Text = "\uE714";
            HintIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            HintPrimary.Text = L10n.SelectorVideoPrimary;
            HintSecondary.Text = L10n.SelectorVideoSecondary;
        }
        else
        {
            HintPrimary.Text = L10n.SelectorScreenPrimary;
            HintSecondary.Text = L10n.SelectorScreenSecondary;
        }

        ScreenImage.Source = screen;
        Shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, _fullGeometry, _holeGeometry);

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplyPhysicalBounds();
            Activate();
            Keyboard.Focus(this);
        };
        Root.SizeChanged += (_, _) =>
        {
            _fullGeometry.Rect = new Rect(0, 0, Root.ActualWidth, Root.ActualHeight);
            PositionHint();
        };
    }

    #region Window placement

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyPhysicalBounds();
    }

    /// <summary>Places the window over the virtual desktop using physical pixels (avoids DIP rounding).</summary>
    private void ApplyPhysicalBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The overlay spans monitors with different DPIs; don't let WPF resize it on DPI changes.
        // Coordinates are mapped to pixels by ratio, so the WPF scale factor doesn't matter.
        if (msg == NativeMethods.WM_DPICHANGED) handled = true;
        return IntPtr.Zero;
    }

    /// <summary>Pixels per DIP inside this window.</summary>
    private (double X, double Y) Scale => (
        _screen.PixelWidth / Math.Max(1.0, Root.ActualWidth),
        _screen.PixelHeight / Math.Max(1.0, Root.ActualHeight));

    /// <summary>Centers the hint banner on the primary monitor.</summary>
    private void PositionHint()
    {
        var (sx, sy) = Scale;
        Canvas.SetLeft(HintHost, -_bounds.X / sx);
        Canvas.SetTop(HintHost, -_bounds.Y / sy);
        HintHost.Width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN) / sx;
    }

    #endregion

    #region Input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = ClampToRoot(e.GetPosition(Root));
        _dragStart = p;
        CaptureMouse();

        CrossH.Visibility = CrossV.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Visible;
        Fade(HintHost, 0);
        UpdateSelection(p, p);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ClampToRoot(e.GetPosition(Root));
        if (_dragStart is Point start)
            UpdateSelection(start, p);
        else
            UpdateCrosshair(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is not Point start) return;

        var end = ClampToRoot(e.GetPosition(Root));
        _dragStart = null;
        ReleaseMouseCapture();

        var region = ToPixels(MakeRect(start, end));
        if (region.Width >= MinSelectionPx && region.Height >= MinSelectionPx)
        {
            SelectedRegion = region;
            DialogResult = true;
        }
        else if (_forVideo)
        {
            SelectedRegion = GetMonitorRegionAtCursor();
            DialogResult = true;
        }
        else
        {
            ResetSelection(end);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_dragStart != null)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            ResetSelection(ClampToRoot(e.GetPosition(Root)));
        }
        else
        {
            DialogResult = false;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragStart != null)
        {
            _dragStart = null;
            ResetSelection(ClampToRoot(Mouse.GetPosition(Root)));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_forVideo && _dragStart is null && e.Key is Key.Enter or Key.Return or Key.Space)
        {
            SelectedRegion = GetMonitorRegionAtCursor();
            DialogResult = true;
            return;
        }

        if (e.Key != Key.Escape) return;

        if (_dragStart != null)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            ResetSelection(ClampToRoot(Mouse.GetPosition(Root)));
        }
        else
        {
            DialogResult = false;
        }
    }

    #endregion

    #region Visuals

    private void UpdateCrosshair(Point p)
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        CrossH.X1 = 0; CrossH.X2 = w; CrossH.Y1 = CrossH.Y2 = p.Y;
        CrossV.Y1 = 0; CrossV.Y2 = h; CrossV.X1 = CrossV.X2 = p.X;
        CrossH.Visibility = CrossV.Visibility = Visibility.Visible;

        var (sx, sy) = Scale;
        int screenX = _bounds.X + (int)Math.Round(p.X * sx);
        int screenY = _bounds.Y + (int)Math.Round(p.Y * sy);
        var size = SetInfoText($"{screenX}, {screenY}");

        double x = p.X + 16, y = p.Y + 20;
        if (x + size.Width > w) x = p.X - size.Width - 12;
        if (y + size.Height > h) y = p.Y - size.Height - 12;
        PlaceInfo(x, y);
    }

    private void UpdateSelection(Point a, Point b)
    {
        var r = MakeRect(a, b);
        _holeGeometry.Rect = r;

        Canvas.SetLeft(SelectionBorder, r.X - 1);
        Canvas.SetTop(SelectionBorder, r.Y - 1);
        SelectionBorder.Width = r.Width + 2;
        SelectionBorder.Height = r.Height + 2;

        var px = ToPixels(r);
        var size = SetInfoText($"{px.Width} × {px.Height}");

        double x = Math.Clamp(r.X, 0, Math.Max(0, Root.ActualWidth - size.Width));
        double y = r.Y - size.Height - 8;
        if (y < 0) y = r.Y + 8; // not enough room above: put it inside the selection
        PlaceInfo(x, y);
    }

    private void ResetSelection(Point cursor)
    {
        _holeGeometry.Rect = new Rect(0, 0, 0, 0);
        SelectionBorder.Visibility = Visibility.Collapsed;
        Fade(HintHost, 1);
        UpdateCrosshair(cursor);
    }

    private Size SetInfoText(string text)
    {
        InfoText.Text = text;
        InfoLabel.Visibility = Visibility.Visible;
        InfoLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return InfoLabel.DesiredSize;
    }

    private void PlaceInfo(double x, double y)
    {
        Canvas.SetLeft(InfoLabel, x);
        Canvas.SetTop(InfoLabel, y);
    }

    private static void Fade(UIElement element, double to) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)));

    #endregion

    #region Geometry helpers

    private Point ClampToRoot(Point p) => new(
        Math.Clamp(p.X, 0, Root.ActualWidth),
        Math.Clamp(p.Y, 0, Root.ActualHeight));

    private static Rect MakeRect(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private Int32Rect ToPixels(Rect r)
    {
        var (sx, sy) = Scale;
        int x1 = Math.Clamp((int)Math.Round(r.Left * sx), 0, _screen.PixelWidth);
        int y1 = Math.Clamp((int)Math.Round(r.Top * sy), 0, _screen.PixelHeight);
        int x2 = Math.Clamp((int)Math.Round(r.Right * sx), 0, _screen.PixelWidth);
        int y2 = Math.Clamp((int)Math.Round(r.Bottom * sy), 0, _screen.PixelHeight);
        return new Int32Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>Full monitor under the cursor in snapshot bitmap coordinates.</summary>
    private Int32Rect GetMonitorRegionAtCursor()
    {
        NativeMethods.GetCursorPos(out var pt);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        IntPtr mon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (NativeMethods.GetMonitorInfo(mon, ref info))
        {
            int x1 = Math.Clamp(info.rcMonitor.Left - _bounds.X, 0, _screen.PixelWidth);
            int y1 = Math.Clamp(info.rcMonitor.Top - _bounds.Y, 0, _screen.PixelHeight);
            int x2 = Math.Clamp(info.rcMonitor.Right - _bounds.X, 0, _screen.PixelWidth);
            int y2 = Math.Clamp(info.rcMonitor.Bottom - _bounds.Y, 0, _screen.PixelHeight);
            if (x2 > x1 && y2 > y1)
                return new Int32Rect(x1, y1, x2 - x1, y2 - y1);
        }
        return new Int32Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight);
    }

    #endregion
}
