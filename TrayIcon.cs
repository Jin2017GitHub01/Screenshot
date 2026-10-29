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
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _videoItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _settingItem;
    private readonly ToolStripMenuItem _exitItem;
    private bool _audioRecording;
    private bool _videoRecording;

    /// <summary>Left-click on the icon, or "Capture Screen" in the menu.</summary>
    public event EventHandler? CaptureRequested;
    /// <summary>"Capture Audio" / "Stop Audio &amp; Save" in the menu.</summary>
    public event EventHandler? RecordToggleRequested;
    /// <summary>"Capture Video" / "Stop Video &amp; Save" in the menu.</summary>
    public event EventHandler? VideoToggleRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip();

        _captureItem = new ToolStripMenuItem(L10n.TrayCaptureScreen, null, (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty));
        _captureItem.Font = new Font(_menu.Font, FontStyle.Bold);
        _menu.Items.Add(_captureItem);

        _recordItem = new ToolStripMenuItem(L10n.TrayCaptureAudio, null, (_, _) => RecordToggleRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_recordItem);

        _videoItem = new ToolStripMenuItem(L10n.TrayCaptureVideo, null, (_, _) => VideoToggleRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_videoItem);

        _openItem = new ToolStripMenuItem(L10n.TrayOpen, null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_openItem);

        _settingItem = new ToolStripMenuItem(L10n.TraySetting, null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_settingItem);

        _menu.Items.Add(new ToolStripSeparator());

        _exitItem = new ToolStripMenuItem(L10n.TrayExit, null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(SystemInformation.SmallIconSize.Width),
            Text = L10n.TrayIdleTooltip,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) CaptureRequested?.Invoke(this, EventArgs.Empty);
        };

        L10n.LanguageChanged += OnLanguageChanged;
        ApplyLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    public void ApplyLanguage()
    {
        _captureItem.Text = L10n.TrayCaptureScreen;
        _recordItem.Text = _audioRecording ? L10n.TrayStopAudio : L10n.TrayCaptureAudio;
        _videoItem.Text = _videoRecording ? L10n.TrayStopVideo : L10n.TrayCaptureVideo;
        _openItem.Text = L10n.TrayOpen;
        _settingItem.Text = L10n.TraySetting;
        _exitItem.Text = L10n.TrayExit;
        UpdateTooltip();
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    public void SetRecording(bool recording)
    {
        _audioRecording = recording;
        _recordItem.Text = recording ? L10n.TrayStopAudio : L10n.TrayCaptureAudio;
        _videoItem.Enabled = !recording;
        UpdateTooltip();
    }

    public void SetVideoRecording(bool recording)
    {
        _videoRecording = recording;
        _videoItem.Text = recording ? L10n.TrayStopVideo : L10n.TrayCaptureVideo;
        _recordItem.Enabled = !recording;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text =
            _videoRecording ? L10n.TrayRecordingVideoTooltip :
            _audioRecording ? L10n.TrayRecordingAudioTooltip :
            L10n.TrayIdleTooltip;
    }

    public void Dispose()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
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
