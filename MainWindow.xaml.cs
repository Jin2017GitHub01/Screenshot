using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Screenshot;

public partial class MainWindow : Window
{
    private BitmapSource? _current;
    private bool _capturing;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        DpiChanged += (_, _) => UpdatePreviewSize();
    }

    #region Capture

    private async void CaptureButton_Click(object sender, RoutedEventArgs e) => await CaptureAsync();

    internal async Task CaptureAsync()
    {
        if (_capturing) return;
        _capturing = true;

        bool wasVisible = IsVisible;
        bool captured = false;
        var previousState = WindowState;
        Hide();
        // Give DWM time to finish hiding the window / tray menu so it isn't in the snapshot.
        await Task.Delay(300);

        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds;
            var screen = ScreenCapture.Capture(bounds);

            var selector = new RegionSelectorWindow(screen, bounds);
            if (selector.ShowDialog() == true)
            {
                captured = true;
                SetCurrent(ScreenCapture.Crop(screen, selector.SelectedRegion));
                if (TryCopyToClipboard())
                    ShowToast("Captured & copied to clipboard");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Capture failed:\n{ex.Message}", "Snip", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Launched from the tray and cancelled: stay quietly in the tray.
            if (wasVisible || captured)
            {
                Show();
                WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState;
                Activate();
            }
            _capturing = false;
        }
    }

    /// <summary>Brings the window back from the tray.</summary>
    internal void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void SetCurrent(BitmapSource image)
    {
        _current = image;
        PreviewImage.Source = image;
        UpdatePreviewSize();

        EmptyState.Visibility = Visibility.Collapsed;
        PreviewHost.Visibility = Visibility.Visible;
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

    #region Copy / Save

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryCopyToClipboard()) ShowToast("Copied to clipboard");
    }

    private bool TryCopyToClipboard()
    {
        if (_current is null) return false;

        // The clipboard can be briefly locked by other apps; retry a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetImage(_current);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        MessageBox.Show("Could not access the clipboard. Please try again.", "Snip",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void SaveAs()
    {
        if (_current is null) return;

        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        System.IO.Directory.CreateDirectory(folder);

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
            BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(_current));

            using var stream = System.IO.File.Create(dialog.FileName);
            encoder.Save(stream);

            ShowToast($"Saved {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the file:\n{ex.Message}", "Snip",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Keyboard shortcuts

    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.N:
                e.Handled = true;
                await CaptureAsync();
                break;
            case Key.C when _current != null:
                e.Handled = true;
                if (TryCopyToClipboard()) ShowToast("Copied to clipboard");
                break;
            case Key.S when _current != null:
                e.Handled = true;
                SaveAs();
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

    /// <summary>Dark title bar to match the app theme (Windows 10 20H1+ / Windows 11).</summary>
    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int captionColor = 0x001A1312; // COLORREF (0x00BBGGRR) for #12131A
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
    }

    #endregion
}
