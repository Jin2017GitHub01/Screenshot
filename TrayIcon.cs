using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Screenshot;

/// <summary>System tray (notification area) icon with a context menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _videoItem;
    private readonly ToolStripMenuItem _recordItem;
    private bool _audioRecording;
    private bool _videoRecording;

    private const string IdleTooltip = "Capture Everything – click to catch a region";

    /// <summary>Left-click on the icon, or "New Capture" in the menu.</summary>
    public event EventHandler? CaptureRequested;
    /// <summary>"Record Video" / "Stop Video &amp; Save" in the menu.</summary>
    public event EventHandler? VideoToggleRequested;
    /// <summary>"Record Audio" / "Stop Recording" in the menu.</summary>
    public event EventHandler? RecordToggleRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip();
        var capture = new ToolStripMenuItem("New Capture", null, (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty));
        capture.Font = new Font(_menu.Font, FontStyle.Bold);
        _menu.Items.Add(capture);
        _videoItem = new ToolStripMenuItem("Record Video", null, (_, _) => VideoToggleRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_videoItem);
        _recordItem = new ToolStripMenuItem("Record Audio", null, (_, _) => RecordToggleRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_recordItem);
        _menu.Items.Add("Open Capture Everything", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(SystemInformation.SmallIconSize.Width),
            Text = IdleTooltip,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) CaptureRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    public void SetRecording(bool recording)
    {
        _audioRecording = recording;
        _recordItem.Text = recording ? "Stop Audio && Save" : "Record Audio";
        _videoItem.Enabled = !recording;
        UpdateTooltip();
    }

    public void SetVideoRecording(bool recording)
    {
        _videoRecording = recording;
        _videoItem.Text = recording ? "Stop Video && Save" : "Record Video";
        _recordItem.Enabled = !recording;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text =
            _videoRecording ? "Capture Everything – recording video…" :
            _audioRecording ? "Capture Everything – recording audio…" :
            IdleTooltip;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false; // removes the icon from the tray immediately
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}

/// <summary>Draws the app icon (gradient tile with selection-corner brackets) at runtime.</summary>
internal static class IconFactory
{
    public static Icon CreateAppIcon(int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = size;
            using (var tile = RoundedRect(new RectangleF(0.5f, 0.5f, s - 1, s - 1), s * 0.24f))
            using (var fill = new LinearGradientBrush(new RectangleF(0, 0, s, s),
                       Color.FromArgb(139, 92, 246), Color.FromArgb(6, 182, 212), LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(fill, tile);
            }

            float m = s * 0.22f, len = s * 0.2f;
            float l = m, t = m, r = s - m, b = s - m;
            using var pen = new Pen(Color.White, Math.Max(1.5f, s * 0.09f))
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[] { new PointF(l, t + len), new PointF(l, t), new PointF(l + len, t) });
            g.DrawLines(pen, new[] { new PointF(r - len, t), new PointF(r, t), new PointF(r, t + len) });
            g.DrawLines(pen, new[] { new PointF(r, b - len), new PointF(r, b), new PointF(r - len, b) });
            g.DrawLines(pen, new[] { new PointF(l + len, b), new PointF(l, b), new PointF(l, b - len) });

            float d = s * 0.16f;
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, (s - d) / 2, (s - d) / 2, d, d);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone(); // clone owns its own handle
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    /// <summary>Converts a GDI icon to a WPF image (for window icons).</summary>
    public static System.Windows.Media.ImageSource ToImageSource(Icon icon)
    {
        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
