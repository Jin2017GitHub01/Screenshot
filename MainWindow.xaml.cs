using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Shapes = System.Windows.Shapes;

namespace Screenshot;

public partial class MainWindow : Window
{
    private BitmapSource? _current;
    private bool _capturing;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.ApplyDarkTitleBar(this);
        DpiChanged += (_, _) => UpdatePreviewSize();
        InitAnnotationTools();
        InitRecording();
        ApplyLanguage();
        L10n.LanguageChanged += (_, _) => ApplyLanguage();
    }

    internal void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow { Icon = Icon };
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            _settingsWindow.Owner = this;
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyLanguage()
    {
        SubtitleText.Text = L10n.AppSubtitle;
        CaptureLabel.Text = L10n.NewCapture;
        CaptureButton.ToolTip = L10n.NewCaptureTip;

        RecordButton.ToolTip = _recorder.IsRecording ? L10n.StopAudioTip : L10n.RecordAudioTip;
        RecordLabel.Text = _recorder.IsRecording
            ? $"{L10n.StopPrefix}  {FormatDuration(_recorder.Elapsed)}"
            : L10n.RecordAudio;

        VideoButton.ToolTip = _videoRecorder.IsRecording ? L10n.StopVideoTip : L10n.RecordVideoTip;
        VideoLabel.Text = _videoRecorder.IsRecording
            ? $"{L10n.StopPrefix}  {FormatDuration(_videoRecorder.Elapsed)}"
            : L10n.RecordVideo;

        EmptyTitleText.Text = L10n.EmptyTitle;
        EmptyClickRun.Text = L10n.EmptyPrefix;
        EmptyCaptureRun.Text = L10n.NewCapture;
        EmptyOrPressRun.Text = L10n.EmptyOrPress;
        EmptySuffixRun.Text = L10n.EmptySuffix;

        TextToolLabel.Text = L10n.ToolText;
        TextToolButton.ToolTip = L10n.ToolTextTip;
        ArrowToolLabel.Text = L10n.ToolArrow;
        ArrowToolButton.ToolTip = L10n.ToolArrowTip;
        foreach (var swatch in ColorSwatches.Children.OfType<Button>())
            swatch.ToolTip = L10n.SwatchTip;
        SmallerButton.ToolTip = L10n.SmallerTip;
        LargerButton.ToolTip = L10n.LargerTip;
        UndoButton.ToolTip = L10n.UndoTip;
        ClearButton.ToolTip = L10n.ClearTip;
        ZoomOutButton.ToolTip = L10n.ZoomOutTip;
        ZoomResetButton.ToolTip = L10n.ZoomResetTip;
        ZoomInButton.ToolTip = L10n.ZoomInTip;

        CopyLabel.Text = L10n.Copy;
        CopyButton.ToolTip = L10n.CopyTip;
        SaveLabel.Text = L10n.SaveAs;
        SaveButton.ToolTip = L10n.SaveAsTip;

        if (_current is null && !_audioBusy && !_videoBusy)
            InfoText.Text = L10n.Ready;
    }

    #region Capture

    private async void CaptureButton_Click(object sender, RoutedEventArgs e) => await CaptureAsync();

    internal async Task CaptureAsync()
    {
        var selection = await SelectRegionOnScreenAsync(
            forVideo: false, showAfterSelection: true, "It got away! Capture failed:");
        if (selection is not var (screen, _, region)) return;

        SetCurrent(ScreenCapture.Crop(screen, region));
        if (TryCopyToClipboard())
            ShowToast(L10n.ToastCaughtCopied);
    }

    /// <summary>
    /// Hides the main window, snapshots the virtual desktop, and opens <see cref="RegionSelectorWindow"/>.
    /// Restores the window automatically on cancel/failure or when <paramref name="showAfterSelection"/> is true.
    /// </summary>
    private async Task<(BitmapSource Screen, Int32Rect Bounds, Int32Rect Region)?> SelectRegionOnScreenAsync(
        bool forVideo, bool showAfterSelection, string errorPrefix)
    {
        if (_capturing) return null;
        _capturing = true;

        bool wasVisible = IsVisible;
        var previousState = WindowState;
        Hide();
        // Give DWM time to finish hiding the window / tray menu so it isn't in the snapshot.
        await Task.Delay(300);

        bool selected = false;
        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds;
            var screen = ScreenCapture.Capture(bounds);
            var selector = new RegionSelectorWindow(screen, bounds, forVideo);
            if (selector.ShowDialog() == true)
            {
                selected = true;
                return (screen, bounds, selector.SelectedRegion);
            }
            return null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{errorPrefix}\n{ex.Message}", "Capture Everything", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            if (selected ? showAfterSelection : wasVisible)
                RestoreWindow(previousState);
            _capturing = false;
        }
    }

    /// <summary>Brings the window back from the tray.</summary>
    internal void ShowFromTray() => RestoreWindow(WindowState);

    private void RestoreWindow(WindowState previousState)
    {
        Show();
        WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState;
        Activate();
    }

    private void SetCurrent(BitmapSource image)
    {
        _current = image;
        PreviewImage.Source = image;
        UpdatePreviewSize();

        // A new capture starts with a clean annotation layer and 100% zoom.
        AnnotationCanvas.Children.Clear();
        _activeText = null;
        _selectedArrow = null;
        ResetZoom();
        SetTool(AnnotationTool.None);

        EmptyState.Visibility = Visibility.Collapsed;
        PreviewHost.Visibility = Visibility.Visible;
        AnnotateBar.Visibility = Visibility.Visible;
        CopyButton.IsEnabled = SaveButton.IsEnabled = true;
        InfoText.Text = $"{image.PixelWidth} × {image.PixelHeight} px  •  {DateTime.Now:HH:mm:ss}";

        AnimatePreviewIn();
    }

    /// <summary>Shows the image at its true physical size (1 image pixel = 1 screen pixel), shrinking if needed.</summary>
    private void UpdatePreviewSize()
    {
        if (_current is null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        PreviewImage.Width = _current.PixelWidth / dpi.DpiScaleX;
        PreviewImage.Height = _current.PixelHeight / dpi.DpiScaleY;
    }

    #endregion

    #region Zoom & pan

    private const double MinZoom = 0.25;
    private const double MaxZoom = 8.0;
    private const double ZoomStep = 1.15;

    private double _zoom = 1.0;
    private double _panX, _panY;
    private bool _panning;
    private Point _panStartMouse;
    private double _panStartX, _panStartY;

    private void PreviewViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_current is null || e.Delta == 0) return;

        // Do not hijack wheel scrolling inside the annotation toolbar itself.
        if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, AnnotateBar))
            return;

        e.Handled = true;
        double factor = Math.Pow(ZoomStep, e.Delta / 120.0);
        ZoomTo(_zoom * factor, e.GetPosition(PreviewViewport));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) =>
        ZoomTo(_zoom * ZoomStep, GetPreviewHostCenter());

    private void ZoomOut_Click(object sender, RoutedEventArgs e) =>
        ZoomTo(_zoom / ZoomStep, GetPreviewHostCenter());

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private void ResetZoom()
    {
        _zoom = 1.0;
        _panX = _panY = 0;
        ApplyZoomTransform();
    }

    private void ZoomTo(double targetZoom, Point anchorInViewport)
    {
        double newZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - 1.0) < 0.03) newZoom = 1.0;
        if (Math.Abs(newZoom - _zoom) < 1e-6) return;

        // Keep the point under the cursor stationary relative to PreviewHost's center.
        Point center = GetPreviewHostCenter();
        double ratio = newZoom / _zoom;
        _panX = anchorInViewport.X - center.X - (anchorInViewport.X - center.X - _panX) * ratio;
        _panY = anchorInViewport.Y - center.Y - (anchorInViewport.Y - center.Y - _panY) * ratio;
        _zoom = newZoom;

        ClampPan();
        ApplyZoomTransform();
    }

    private Point GetPreviewHostCenter()
    {
        var m = PreviewHost.Margin;
        double w = Math.Max(0, PreviewViewport.ActualWidth - m.Left - m.Right);
        double h = Math.Max(0, PreviewViewport.ActualHeight - m.Top - m.Bottom);
        return new Point(m.Left + w / 2.0, m.Top + h / 2.0);
    }

    private void ClampPan()
    {
        if (_zoom <= 1.0)
        {
            _panX = _panY = 0;
            return;
        }

        double hostW = Math.Max(100, PreviewHost.ActualWidth);
        double hostH = Math.Max(100, PreviewHost.ActualHeight);
        double maxPanX = Math.Max(0, (hostW * _zoom - hostW * 0.25) / 2.0);
        double maxPanY = Math.Max(0, (hostH * _zoom - hostH * 0.25) / 2.0);
        _panX = Math.Clamp(_panX, -maxPanX, maxPanX);
        _panY = Math.Clamp(_panY, -maxPanY, maxPanY);
    }

    private void ApplyZoomTransform()
    {
        ZoomScale.ScaleX = ZoomScale.ScaleY = _zoom;
        ZoomTranslate.X = _panX;
        ZoomTranslate.Y = _panY;
        ZoomResetButton.Content = $"{Math.Round(_zoom * 100):0}%";
        RenderOptions.SetBitmapScalingMode(
            PreviewImage,
            _zoom >= 2.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        UpdateCanvasCursor();
    }

    private void PreviewViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_current is null) return;
        if (e.OriginalSource is DependencyObject d && IsDescendantOf(d, AnnotateBar)) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            if (e.ClickCount == 2) ResetZoom();
            else if (_zoom > 1.0) StartPan(e.GetPosition(PreviewViewport));
        }
        else if (e.ChangedButton == MouseButton.Left && _zoom > 1.0 && _tool == AnnotationTool.None)
        {
            e.Handled = true;
            EndTextEditing();
            SelectArrow(null);
            StartPan(e.GetPosition(PreviewViewport));
        }
    }

    private void StartPan(Point mouseInViewport)
    {
        _panning = true;
        _panStartMouse = mouseInViewport;
        _panStartX = _panX;
        _panStartY = _panY;
        PreviewViewport.CaptureMouse();
        UpdateCanvasCursor();
    }

    private void PreviewViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var delta = e.GetPosition(PreviewViewport) - _panStartMouse;
        _panX = _panStartX + delta.X;
        _panY = _panStartY + delta.Y;
        ClampPan();
        ApplyZoomTransform();
    }

    private void PreviewViewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            e.Handled = true;
            PreviewViewport.ReleaseMouseCapture();
        }
    }

    private void PreviewViewport_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        UpdateCanvasCursor();
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    #endregion

    #region Copy / Save

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryCopyToClipboard()) ShowToast(L10n.ToastCopied);
    }

    private bool TryCopyToClipboard()
    {
        var output = GetOutputImage();
        if (output is null) return false;

        // The clipboard can be briefly locked by other apps; retry a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetImage(output);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        MessageBox.Show("Could not access the clipboard. Please try again.", "Capture Everything",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void SaveAs()
    {
        var output = GetOutputImage();
        if (output is null) return;

        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        Directory.CreateDirectory(folder);

        var dialog = new SaveFileDialog
        {
            Title = "Save screenshot",
            InitialDirectory = folder,
            FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".png",
            AddExtension = true,
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg;*.jpeg|Bitmap image (*.bmp)|*.bmp"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(output));

            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);

            ShowToast(L10n.ToastSaved(Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the file:\n{ex.Message}", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Text annotations

    private static readonly Color[] TextPalette =
    {
        Color.FromRgb(0xEF, 0x44, 0x44), // red
        Color.FromRgb(0xFA, 0xCC, 0x15), // yellow
        Color.FromRgb(0x22, 0xC5, 0x5E), // green
        Color.FromRgb(0x3B, 0x82, 0xF6), // blue
        Color.FromRgb(0xA8, 0x55, 0xF7), // purple
        Colors.White,
        Color.FromRgb(0x11, 0x11, 0x11), // black
    };

    private static readonly double[] TextSizes = { 12, 14, 16, 20, 24, 28, 32, 40, 48, 64, 80 };

    private static readonly Brush FocusChromeBrush = Frozen(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly Brush HoverChromeBrush = Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

    private enum AnnotationTool { None, Text, Arrow }

    /// <summary>Model behind an arrow <see cref="Shapes.Path"/> (stored in its Tag).</summary>
    private sealed class ArrowData
    {
        public Point Start, End;
        public Color Color;
        public int SizeIndex;
    }

    private AnnotationTool _tool;
    private Color _textColor = TextPalette[0];
    private int _textSizeIndex = 4; // 24
    private TextBox? _activeText;
    private bool _exporting;

    private Border? _dragging;
    private bool _dragMoved;
    private Point _dragStart, _dragOrigin;

    private Shapes.Path? _drawingArrow;   // arrow currently being drawn
    private Shapes.Path? _draggingArrow;  // arrow currently being moved
    private Shapes.Path? _selectedArrow;
    private Point _arrowOriginStart, _arrowOriginEnd;

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private void InitAnnotationTools()
    {
        foreach (var color in TextPalette)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Background = Frozen(color),
                Tag = color,
                ToolTip = "Color (text & arrows)"
            };
            swatch.Click += (_, _) => SetTextColor(color);
            ColorSwatches.Children.Add(swatch);
        }
        UpdateToolUi();
    }

    private void TextToolButton_Click(object sender, RoutedEventArgs e) => ToggleTool(AnnotationTool.Text);
    private void ArrowToolButton_Click(object sender, RoutedEventArgs e) => ToggleTool(AnnotationTool.Arrow);

    private void ToggleTool(AnnotationTool tool) => SetTool(_tool == tool ? AnnotationTool.None : tool);

    private void SetTool(AnnotationTool tool)
    {
        _tool = tool;
        UpdateCanvasCursor();
        StyleToolButton(TextToolButton, tool == AnnotationTool.Text);
        StyleToolButton(ArrowToolButton, tool == AnnotationTool.Arrow);

        if (tool == AnnotationTool.Text) ShowToast(L10n.ToastTextHint);
        else if (tool == AnnotationTool.Arrow) ShowToast(L10n.ToastArrowHint);
    }

    private void UpdateCanvasCursor()
    {
        AnnotationCanvas.Cursor = _tool switch
        {
            AnnotationTool.Text => Cursors.IBeam,
            AnnotationTool.Arrow => Cursors.Cross,
            _ => _panning ? Cursors.SizeAll : _zoom > 1.0 ? Cursors.Hand : null
        };
    }

    private void StyleToolButton(Button button, bool active)
    {
        if (active)
        {
            button.Background = (Brush)FindResource("AccentBrush");
            button.Foreground = Brushes.White;
        }
        else
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(ForegroundProperty);
        }
    }

    private void SetTextColor(Color color)
    {
        _textColor = color;
        if (_activeText != null) ApplyTextStyle(_activeText);
        if (_selectedArrow != null) ApplyArrowStyle(_selectedArrow);
        UpdateToolUi();
    }

    private void FontSmaller_Click(object sender, RoutedEventArgs e) => ChangeTextSize(-1);
    private void FontLarger_Click(object sender, RoutedEventArgs e) => ChangeTextSize(+1);

    private void ChangeTextSize(int delta)
    {
        _textSizeIndex = Math.Clamp(_textSizeIndex + delta, 0, TextSizes.Length - 1);
        if (_activeText != null) ApplyTextStyle(_activeText);
        if (_selectedArrow != null) ApplyArrowStyle(_selectedArrow);
        UpdateToolUi();
    }

    private void ClearText_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Children.Clear();
        _activeText = null;
        _selectedArrow = null;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastAnnotation();

    private void UndoLastAnnotation()
    {
        EndTextEditing();
        int count = AnnotationCanvas.Children.Count;
        if (count == 0) return;
        var last = AnnotationCanvas.Children[count - 1];
        AnnotationCanvas.Children.RemoveAt(count - 1);
        if (last == _selectedArrow) _selectedArrow = null;
    }

    private void UpdateToolUi()
    {
        foreach (var swatch in ColorSwatches.Children.OfType<Button>())
            swatch.BorderBrush = swatch.Tag is Color c && c == _textColor ? Brushes.White : Brushes.Transparent;
        FontSizeText.Text = TextSizes[_textSizeIndex].ToString("0");
    }

    private void ApplyTextStyle(TextBox box)
    {
        var brush = Frozen(_textColor);
        box.Foreground = brush;
        box.CaretBrush = brush;
        box.FontSize = TextSizes[_textSizeIndex];
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != AnnotationCanvas) return;
        e.Handled = true;

        bool wasTyping = IsEditingText(out var editing) && !string.IsNullOrWhiteSpace(editing!.Text);
        EndTextEditing();
        SelectArrow(null);

        var pos = e.GetPosition(AnnotationCanvas);
        switch (_tool)
        {
            case AnnotationTool.Text:
                AddTextAnnotation(pos);
                break;
            case AnnotationTool.Arrow:
                _drawingArrow = CreateArrow(pos);
                AnnotationCanvas.CaptureMouse();
                break;
            case AnnotationTool.None when !wasTyping && _zoom > 1.0:
                StartPan(e.GetPosition(PreviewViewport));
                break;
        }
    }

    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drawingArrow is null) return;
        ((ArrowData)_drawingArrow.Tag).End = e.GetPosition(AnnotationCanvas);
        UpdateArrowGeometry(_drawingArrow);
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawingArrow is null) return;
        e.Handled = true;
        AnnotationCanvas.ReleaseMouseCapture(); // finishes in LostMouseCapture
    }

    private void AnnotationCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource != AnnotationCanvas || _drawingArrow is null) return;
        var arrow = _drawingArrow;
        _drawingArrow = null;

        // A plain click (no real drag) doesn't leave a stray dot behind.
        var d = (ArrowData)arrow.Tag;
        if ((d.End - d.Start).Length < 6) AnnotationCanvas.Children.Remove(arrow);
    }

    private Shapes.Path CreateArrow(Point start)
    {
        var path = new Shapes.Path
        {
            Tag = new ArrowData { Start = start, End = start, Color = _textColor, SizeIndex = _textSizeIndex },
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Cursor = Cursors.SizeAll,
            Effect = AnnotationShadow()
        };
        ApplyArrowStyle(path);

        path.MouseLeftButtonDown += (_, e) =>
        {
            if (_drawingArrow != null) return;
            EndTextEditing();
            SelectArrow(path);
            var d = (ArrowData)path.Tag;
            _draggingArrow = path;
            _dragStart = e.GetPosition(AnnotationCanvas);
            _arrowOriginStart = d.Start;
            _arrowOriginEnd = d.End;
            path.CaptureMouse();
            e.Handled = true;
        };
        path.MouseMove += (_, e) =>
        {
            if (_draggingArrow != path) return;
            var delta = e.GetPosition(AnnotationCanvas) - _dragStart;
            var d = (ArrowData)path.Tag;
            d.Start = _arrowOriginStart + delta;
            d.End = _arrowOriginEnd + delta;
            UpdateArrowGeometry(path);
        };
        path.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingArrow != path) return;
            _draggingArrow = null;
            path.ReleaseMouseCapture();
            e.Handled = true;
        };

        AnnotationCanvas.Children.Add(path);
        return path;
    }

    /// <summary>Applies the toolbar's current color/size to an arrow.</summary>
    private void ApplyArrowStyle(Shapes.Path path)
    {
        var d = (ArrowData)path.Tag;
        d.Color = _textColor;
        d.SizeIndex = _textSizeIndex;
        var brush = Frozen(d.Color);
        path.Stroke = brush;
        path.Fill = brush;
        path.StrokeThickness = ArrowThickness(d.SizeIndex);
        UpdateArrowGeometry(path);
    }

    /// <summary>Arrow thickness follows the shared size control (24 → 4 px, 80 → ~13 px).</summary>
    private static double ArrowThickness(int sizeIndex) => Math.Max(2, TextSizes[sizeIndex] / 6);

    private static void UpdateArrowGeometry(Shapes.Path path)
    {
        var d = (ArrowData)path.Tag;
        var geometry = new PathGeometry();
        Vector v = d.End - d.Start;
        double length = v.Length;
        if (length >= 0.5)
        {
            v /= length;
            double thickness = ArrowThickness(d.SizeIndex);
            double headLength = Math.Min(length, Math.Max(10, thickness * 3.5));
            double headHalfWidth = headLength * 0.6;
            Point headBase = d.End - v * headLength;
            var normal = new Vector(-v.Y, v.X);

            // Shaft (stroke only), stopping inside the head so the tip stays sharp.
            var shaft = new PathFigure { StartPoint = d.Start, IsFilled = false };
            shaft.Segments.Add(new LineSegment(headBase + v * Math.Min(thickness, headLength / 2), true));
            geometry.Figures.Add(shaft);

            // Filled triangular head.
            var head = new PathFigure { StartPoint = d.End, IsClosed = true, IsFilled = true };
            head.Segments.Add(new LineSegment(headBase + normal * headHalfWidth, true));
            head.Segments.Add(new LineSegment(headBase - normal * headHalfWidth, true));
            geometry.Figures.Add(head);
        }
        path.Data = geometry;
    }

    private void SelectArrow(Shapes.Path? arrow)
    {
        _selectedArrow = arrow;
        if (arrow?.Tag is ArrowData d)
        {
            // Reflect the selected arrow's style in the toolbar.
            _textColor = d.Color;
            _textSizeIndex = d.SizeIndex;
            UpdateToolUi();
        }
        RefreshChrome();
    }

    private static System.Windows.Media.Effects.DropShadowEffect AnnotationShadow() =>
        new() { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.55, Color = Colors.Black };

    private static System.Windows.Media.Effects.DropShadowEffect SelectionGlow() =>
        new() { BlurRadius = 12, ShadowDepth = 0, Opacity = 1, Color = Color.FromRgb(0x8B, 0x5C, 0xF6) };

    /// <summary>Updates selection / hover visuals of every annotation (hidden while exporting).</summary>
    private void RefreshChrome()
    {
        foreach (var child in AnnotationCanvas.Children)
        {
            if (child is Border holder) UpdateChrome(holder);
            else if (child is Shapes.Path path)
                path.Effect = !_exporting && path == _selectedArrow ? SelectionGlow() : AnnotationShadow();
        }
    }

    private void AddTextAnnotation(Point at)
    {
        var box = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = 12,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            SelectionBrush = FocusChromeBrush,
            Effect = AnnotationShadow()
        };
        ApplyTextStyle(box);

        // The border is the move handle (drag its edge) and shows the editing chrome.
        var holder = new Border
        {
            Child = box,
            Padding = new Thickness(6, 3, 6, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.SizeAll,
            ToolTip = null
        };
        Canvas.SetLeft(holder, at.X - 7);
        Canvas.SetTop(holder, at.Y - box.FontSize * 0.7);

        box.GotKeyboardFocus += (_, _) =>
        {
            SelectArrow(null);
            _activeText = box;
            // Reflect this text's style in the toolbar.
            if (box.Foreground is SolidColorBrush b) _textColor = b.Color;
            int idx = Array.IndexOf(TextSizes, box.FontSize);
            if (idx >= 0) _textSizeIndex = idx;
            UpdateToolUi();
            UpdateChrome(holder);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (_activeText == box) _activeText = null;
            if (string.IsNullOrWhiteSpace(box.Text)) AnnotationCanvas.Children.Remove(holder);
            else UpdateChrome(holder);
        };
        holder.MouseEnter += (_, _) => UpdateChrome(holder);
        holder.MouseLeave += (_, _) => UpdateChrome(holder);

        holder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != holder) return; // clicks on the text itself go to the TextBox
            _dragging = holder;
            _dragMoved = false;
            _dragStart = e.GetPosition(AnnotationCanvas);
            _dragOrigin = new Point(Canvas.GetLeft(holder), Canvas.GetTop(holder));
            holder.CaptureMouse();
            e.Handled = true;
        };
        holder.MouseMove += (_, e) =>
        {
            if (_dragging != holder) return;
            var delta = e.GetPosition(AnnotationCanvas) - _dragStart;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 2) _dragMoved = true;
            Canvas.SetLeft(holder, _dragOrigin.X + delta.X);
            Canvas.SetTop(holder, _dragOrigin.Y + delta.Y);
        };
        holder.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragging != holder) return;
            _dragging = null;
            holder.ReleaseMouseCapture();
            if (!_dragMoved)
            {
                box.Focus();
                box.CaretIndex = box.Text.Length;
            }
            e.Handled = true;
        };

        box.Loaded += (_, _) =>
        {
            box.Focus();
            Keyboard.Focus(box);
        };
        AnnotationCanvas.Children.Add(holder);
    }

    private void UpdateChrome(Border holder)
    {
        holder.BorderBrush =
            _exporting ? Brushes.Transparent :
            holder.IsKeyboardFocusWithin ? FocusChromeBrush :
            holder.IsMouseOver ? HoverChromeBrush :
            Brushes.Transparent;
    }

    private bool IsEditingText(out TextBox? box)
    {
        box = Keyboard.FocusedElement as TextBox;
        return box?.Parent is Border b && AnnotationCanvas.Children.Contains(b);
    }

    private void EndTextEditing()
    {
        if (!IsEditingText(out _)) return;
        Keyboard.ClearFocus();
        Focus();
    }

    /// <summary>The captured image with all annotations burned in at full pixel resolution.</summary>
    private BitmapSource? GetOutputImage()
    {
        if (_current is null) return null;

        EndTextEditing();
        foreach (var empty in AnnotationCanvas.Children.OfType<Border>()
                     .Where(h => h.Child is TextBox t && string.IsNullOrWhiteSpace(t.Text)).ToList())
            AnnotationCanvas.Children.Remove(empty);

        double w = AnnotationCanvas.ActualWidth, h = AnnotationCanvas.ActualHeight;
        if (AnnotationCanvas.Children.Count == 0 || w <= 0 || h <= 0) return _current;

        _exporting = true;
        try
        {
            RefreshChrome();
            AnnotationCanvas.UpdateLayout();

            int pw = _current.PixelWidth, ph = _current.PixelHeight;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(_current, new Rect(0, 0, pw, ph));
                // Canvas is laid out in DIPs; scale it up to the bitmap's physical pixels.
                dc.PushTransform(new ScaleTransform(pw / w, ph / h));
                dc.DrawRectangle(new VisualBrush(AnnotationCanvas)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, w, h)
                }, null, new Rect(0, 0, w, h));
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var result = new WriteableBitmap(new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0));
            result.Freeze();
            return result;
        }
        finally
        {
            _exporting = false;
            RefreshChrome();
        }
    }

    #endregion

    #region Audio & video recording

    private readonly AudioRecorder _recorder = new();
    private readonly VideoRecorder _videoRecorder = new();
    private readonly System.Windows.Threading.DispatcherTimer _recordingTimer =
        new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RecordingOverlayWindow? _videoOverlay;
    private bool _audioBusy;
    private bool _videoBusy;
    private double _level;

    public bool IsRecording => _recorder.IsRecording;
    public bool IsVideoRecording => _videoRecorder.IsRecording;

    /// <summary>Raised when audio recording starts (true) or stops (false); used by the tray menu.</summary>
    public event EventHandler<bool>? RecordingStateChanged;

    /// <summary>Raised when video recording starts (true) or stops (false); used by the tray menu.</summary>
    public event EventHandler<bool>? VideoRecordingStateChanged;

    private void InitRecording()
    {
        _recordingTimer.Tick += (_, _) => UpdateRecordingTick();
        _recorder.StoppedUnexpectedly += (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            if (_audioBusy) return;
            ShowFromTray();
            MessageBox.Show(this, "Recording stopped because the audio output device changed or became unavailable.\n" +
                                  "You can still save what was recorded so far.", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            await StopRecordingAsync();
        });
        _videoRecorder.StoppedUnexpectedly += (_, ex) => Dispatcher.InvokeAsync(async () =>
        {
            if (_videoBusy) return;
            CloseVideoOverlay();
            ShowFromTray();
            MessageBox.Show(this,
                $"Video recording stopped unexpectedly:\n{ex.Message}\n\nYou can still save what was recorded so far.",
                "Capture Everything", MessageBoxButton.OK, MessageBoxImage.Warning);
            await StopVideoRecordingAsync();
        });
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e) => await ToggleRecordingAsync();
    private async void VideoButton_Click(object sender, RoutedEventArgs e) => await ToggleVideoRecordingAsync();

    internal async Task ToggleRecordingAsync()
    {
        if (_audioBusy || _videoRecorder.IsRecording || _videoBusy) return;
        if (_recorder.IsRecording) await StopRecordingAsync();
        else StartRecording();
    }

    internal async Task ToggleVideoRecordingAsync()
    {
        if (_videoBusy || _audioBusy || _recorder.IsRecording) return;
        if (_videoRecorder.IsRecording) await StopVideoRecordingAsync();
        else await StartVideoRecordingAsync();
    }

    private void StartRecording()
    {
        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start audio recording:\n{ex.Message}", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetRecordingUi(true);
        ShowToast(L10n.ToastRecordingAudio);
    }

    /// <summary>Stops audio recording and (unless <paramref name="save"/> is false) asks where to save the MP3.</summary>
    private async Task StopRecordingAsync(bool save = true)
    {
        if (_audioBusy) return;
        _audioBusy = true;
        RecordButton.IsEnabled = false;
        try
        {
            var duration = _recorder.Elapsed;
            string? wav = await _recorder.StopAsync();
            SetRecordingUi(false);

            if (!save) return;
            if (wav is null)
            {
                ShowToast(L10n.ToastNothingRecorded);
                return;
            }
            await SaveRecordingAsync(wav, duration);
        }
        finally
        {
            _recorder.DeleteTempFile();
            RecordButton.IsEnabled = !_videoRecorder.IsRecording;
            _audioBusy = false;
        }
    }

    private async Task SaveRecordingAsync(string wav, TimeSpan duration)
    {
        string? target = PromptSaveRecordingPath(
            Environment.SpecialFolder.MyMusic, "Recordings",
            $"Save audio recording ({FormatDuration(duration)})",
            "Recording", ".mp3", "MP3 audio (*.mp3)|*.mp3",
            L10n.DiscardAudioQuestion, L10n.ToastAudioDiscarded);
        if (target is null) return;

        string previousInfo = InfoText.Text;
        InfoText.Text = L10n.SavingMp3;
        try
        {
            await Task.Run(() => AudioRecorder.EncodeMp3(wav, target));
            ShowToast(L10n.ToastSaved(Path.GetFileName(target)));
        }
        catch (Exception ex)
        {
            AudioRecorder.DeleteFileBestEffort(target);
            string wavTarget = Path.ChangeExtension(target, ".wav");
            if (MessageBox.Show(this,
                    $"Could not create the MP3 file:\n{ex.Message}\n\n" +
                    $"Save the recording as a WAV file instead?\n{wavTarget}", "Capture Everything",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Copy(wav, wavTarget, overwrite: true);
                    ShowToast(L10n.ToastSaved(Path.GetFileName(wavTarget)));
                }
                catch (Exception copyEx)
                {
                    MessageBox.Show(this, $"Could not save the file:\n{copyEx.Message}", "Capture Everything",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        finally
        {
            InfoText.Text = previousInfo;
        }
    }

    private async Task StartVideoRecordingAsync()
    {
        if (_videoBusy || _recorder.IsRecording || _videoRecorder.IsRecording) return;
        bool wasVisible = IsVisible;
        var previousState = WindowState;

        var selection = await SelectRegionOnScreenAsync(
            forVideo: true, showAfterSelection: false, "Could not select video region:");
        if (selection is not var (_, bounds, r)) return;

        var region = new Int32Rect(bounds.X + r.X, bounds.Y + r.Y, r.Width, r.Height);
        try
        {
            _videoRecorder.Start(region);
        }
        catch (Exception ex)
        {
            if (wasVisible) RestoreWindow(previousState);
            MessageBox.Show($"Could not start video recording:\n{ex.Message}", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _videoOverlay = new RecordingOverlayWindow(region);
        _videoOverlay.StopRequested += async (_, _) => await StopVideoRecordingAsync();
        _videoOverlay.ShowOverlay();
        SetVideoRecordingUi(true);
    }

    private async Task StopVideoRecordingAsync(bool save = true)
    {
        if (_videoBusy) return;
        _videoBusy = true;
        VideoButton.IsEnabled = false;
        CloseVideoOverlay();

        try
        {
            var duration = _videoRecorder.Elapsed;
            var region = _videoRecorder.Region;
            string? mp4 = await _videoRecorder.StopAsync();
            SetVideoRecordingUi(false);

            if (!save) return;

            ShowFromTray();
            if (mp4 is null)
            {
                ShowToast(L10n.ToastNothingRecorded);
                return;
            }
            await SaveVideoRecordingAsync(mp4, duration, region);
        }
        finally
        {
            _videoRecorder.DeleteTempFile();
            VideoButton.IsEnabled = !_recorder.IsRecording;
            _videoBusy = false;
        }
    }

    private async Task SaveVideoRecordingAsync(string mp4, TimeSpan duration, Int32Rect region)
    {
        string? target = PromptSaveRecordingPath(
            Environment.SpecialFolder.MyVideos, "Captures",
            $"Save video recording ({FormatDuration(duration)})",
            "Video", ".mp4", "MP4 video (*.mp4)|*.mp4",
            L10n.DiscardVideoQuestion, L10n.ToastVideoDiscarded);
        if (target is null) return;

        string previousInfo = InfoText.Text;
        InfoText.Text = L10n.SavingMp4;
        try
        {
            await Task.Run(() => File.Copy(mp4, target, overwrite: true));
            InfoText.Text = $"{region.Width} × {region.Height} px  •  {FormatDuration(duration)}  •  {DateTime.Now:HH:mm:ss}";
            ShowToast(L10n.ToastSaved(Path.GetFileName(target)));
        }
        catch (Exception ex)
        {
            InfoText.Text = previousInfo;
            MessageBox.Show(this, $"Could not save the video file:\n{ex.Message}", "Capture Everything",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? PromptSaveRecordingPath(
        Environment.SpecialFolder baseFolder, string subfolder, string title,
        string filePrefix, string defaultExt, string filter,
        string discardQuestion, string discardedToast)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) ShowFromTray();

        string folder = Path.Combine(Environment.GetFolderPath(baseFolder), subfolder);
        Directory.CreateDirectory(folder);

        var dialog = new SaveFileDialog
        {
            Title = title,
            InitialDirectory = folder,
            FileName = $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = defaultExt,
            AddExtension = true,
            Filter = filter
        };

        // Cancelling would lose the recording, so confirm first.
        while (dialog.ShowDialog(this) != true)
        {
            if (MessageBox.Show(this, discardQuestion, "Capture Everything",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ShowToast(discardedToast);
                return null;
            }
        }
        return dialog.FileName;
    }

    /// <summary>Called before the app exits. Returns false if the user cancels the exit.</summary>
    internal async Task<bool> ConfirmExitWhileRecordingAsync()
    {
        if (!_videoRecorder.IsRecording && !_recorder.IsRecording)
            return !_audioBusy && !_videoBusy;

        bool isVideo = _videoRecorder.IsRecording;
        ShowFromTray();
        var answer = MessageBox.Show(this,
            isVideo ? L10n.ExitWhileVideoRecording : L10n.ExitWhileAudioRecording,
            "Capture Everything", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;

        bool save = answer == MessageBoxResult.Yes;
        if (isVideo) await StopVideoRecordingAsync(save);
        else await StopRecordingAsync(save);
        return true;
    }

    internal void DisposeRecorder()
    {
        try { _settingsWindow?.Close(); } catch { /* best effort */ }
        CloseVideoOverlay();
        _videoRecorder.Dispose();
        _recorder.Dispose();
    }

    private void CloseVideoOverlay()
    {
        if (_videoOverlay is null) return;
        var overlay = _videoOverlay;
        _videoOverlay = null;
        try { overlay.Close(); } catch { /* best effort */ }
    }

    private void SetRecordingUi(bool recording)
    {
        ApplyRecordingButtonVisuals(RecordButton, RecordIcon, RecordDot, RecordLabel,
            recording, L10n.RecordAudio, L10n.RecordAudioTip, L10n.StopAudioTip);
        LevelMeter.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        LevelFill.Width = 0;
        VideoButton.IsEnabled = !recording;
        RecordingStateChanged?.Invoke(this, recording);
    }

    private void SetVideoRecordingUi(bool recording)
    {
        ApplyRecordingButtonVisuals(VideoButton, VideoIcon, VideoDot, VideoLabel,
            recording, L10n.RecordVideo, L10n.RecordVideoTip, L10n.StopVideoTip);
        RecordButton.IsEnabled = !recording;
        VideoRecordingStateChanged?.Invoke(this, recording);
    }

    private void ApplyRecordingButtonVisuals(
        Button button, UIElement icon, UIElement dot, TextBlock label,
        bool recording, string idleLabel, string idleTip, string stopTip)
    {
        if (recording)
        {
            button.Background = (Brush)FindResource("RecordBrush");
            button.BorderBrush = button.Background;
            button.Foreground = Brushes.White;
            button.ToolTip = stopTip;
            icon.Visibility = Visibility.Collapsed;
            dot.Visibility = Visibility.Visible;
            label.Text = $"{L10n.StopPrefix}  0:00";
            dot.BeginAnimation(OpacityProperty, CreatePulseAnimation());
            _level = 0;
            _recordingTimer.Start();
        }
        else
        {
            _recordingTimer.Stop();
            dot.BeginAnimation(OpacityProperty, null);
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(ForegroundProperty);
            button.ToolTip = idleTip;
            icon.Visibility = Visibility.Visible;
            dot.Visibility = Visibility.Collapsed;
            label.Text = idleLabel;
        }
    }

    internal static DoubleAnimation CreatePulseAnimation() =>
        new(1, 0.25, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

    private void UpdateRecordingTick()
    {
        if (_recorder.IsRecording)
        {
            RecordLabel.Text = $"{L10n.StopPrefix}  {FormatDuration(_recorder.Elapsed)}";
            _level = Math.Max(Math.Sqrt(_recorder.ReadPeak()), _level * 0.85);
            LevelFill.Width = LevelMeter.Width * _level;
        }
        else if (_videoRecorder.IsRecording)
        {
            var elapsed = _videoRecorder.Elapsed;
            VideoLabel.Text = $"{L10n.StopPrefix}  {FormatDuration(elapsed)}";
            _level = Math.Max(Math.Sqrt(_videoRecorder.ReadPeak()), _level * 0.85);
            _videoOverlay?.UpdateStatus(elapsed, _level);
        }
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    #endregion

    #region Keyboard shortcuts

    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool editing = IsEditingText(out _);

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Escape && editing)
            {
                e.Handled = true;
                EndTextEditing();
            }
            else if (e.Key == Key.Escape && _selectedArrow != null)
            {
                e.Handled = true;
                SelectArrow(null);
            }
            else if (e.Key == Key.Escape && _tool != AnnotationTool.None)
            {
                e.Handled = true;
                SetTool(AnnotationTool.None);
            }
            else if (e.Key is Key.Delete or Key.Back && !editing && _selectedArrow != null)
            {
                e.Handled = true;
                AnnotationCanvas.Children.Remove(_selectedArrow);
                _selectedArrow = null;
            }
            else if (e.Key == Key.T && !editing && _current != null)
            {
                e.Handled = true;
                ToggleTool(AnnotationTool.Text);
            }
            else if (e.Key == Key.A && !editing && _current != null)
            {
                e.Handled = true;
                ToggleTool(AnnotationTool.Arrow);
            }
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.R)
        {
            e.Handled = true;
            await ToggleVideoRecordingAsync();
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.Enter when editing:
                e.Handled = true;
                EndTextEditing();
                break;
            case Key.Z when !editing && _current != null:
                e.Handled = true;
                UndoLastAnnotation();
                break;
            case Key.N:
                e.Handled = true;
                await CaptureAsync();
                break;
            case Key.R:
                e.Handled = true;
                await ToggleRecordingAsync();
                break;
            case Key.C when _current != null && !editing:
                e.Handled = true;
                if (TryCopyToClipboard()) ShowToast(L10n.ToastCopied);
                break;
            case Key.S when _current != null:
                e.Handled = true;
                SaveAs();
                break;
            case Key.D0 or Key.NumPad0 when _current != null && !editing:
                e.Handled = true;
                ResetZoom();
                break;
            case Key.OemPlus or Key.Add when _current != null && !editing:
                e.Handled = true;
                ZoomTo(_zoom * ZoomStep, GetPreviewHostCenter());
                break;
            case Key.OemMinus or Key.Subtract when _current != null && !editing:
                e.Handled = true;
                ZoomTo(_zoom / ZoomStep, GetPreviewHostCenter());
                break;
        }
    }

    #endregion

    #region Visual polish

    private void AnimatePreviewIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);

        PreviewHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var scale = new DoubleAnimation(0.94, 1, duration) { EasingFunction = ease };
        PreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        Toast.BeginAnimation(OpacityProperty, animation);
    }

    #endregion
}
