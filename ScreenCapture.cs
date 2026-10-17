using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Screenshot.NativeMethods;

namespace Screenshot;

/// <summary>GDI-based screen capture helpers.</summary>
public static class ScreenCapture
{
    /// <summary>Bounds of the whole virtual desktop (all monitors) in physical pixels.</summary>
    public static Int32Rect VirtualScreenBounds => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>Captures the given screen rectangle (physical pixels) into a frozen bitmap.</summary>
    public static BitmapSource Capture(Int32Rect area)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = CreateCompatibleBitmap(screenDc, area.Width, area.Height);
        IntPtr oldObject = SelectObject(memDc, hBitmap);
        try
        {
            if (!BitBlt(memDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed while capturing the screen.");

            SelectObject(memDc, oldObject);

            BitmapSource raw = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // Normalize to opaque Bgr32 and materialize so the HBITMAP can be freed safely.
            var result = new WriteableBitmap(new FormatConvertedBitmap(raw, PixelFormats.Bgr32, null, 0));
            result.Freeze();
            return result;
        }
        finally
        {
            SelectObject(memDc, oldObject);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Crops a bitmap to the given pixel rectangle.</summary>
    public static BitmapSource Crop(BitmapSource source, Int32Rect region)
    {
        var cropped = new WriteableBitmap(new CroppedBitmap(source, region));
        cropped.Freeze();
        return cropped;
    }
}
