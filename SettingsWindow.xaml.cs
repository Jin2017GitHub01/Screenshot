using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Screenshot;

public partial class SettingsWindow : Window
{
    private readonly List<(AppLanguage Language, Button Button, TextBlock Check)> _languageButtons = [];

    public SettingsWindow()
    {
        InitializeComponent();
        BuildLanguageButtons();
        SourceInitialized += (_, _) => NativeMethods.ApplyDarkTitleBar(this);
        Closed += (_, _) =>
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            L10n.PrintScreenKeyChanged -= OnPrintScreenKeyChanged;
        };
        L10n.LanguageChanged += OnLanguageChanged;
        L10n.PrintScreenKeyChanged += OnPrintScreenKeyChanged;
        ApplyLanguage();
        ApplyPrintScreenState();
    }

    private void BuildLanguageButtons()
    {
        var iconFont = (FontFamily)FindResource("IconFont");
        var buttonStyle = (Style)FindResource("BaseButton");

        foreach (var (lang, _, nativeName) in L10n.SupportedLanguages)
        {
            var check = new TextBlock
            {
                Text = "\uE73E",
                FontFamily = iconFont,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Collapsed
            };

            var label = new TextBlock
            {
                Text = nativeName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(check);
            panel.Children.Add(label);

            var btn = new Button
            {
                Style = buttonStyle,
                Padding = new Thickness(10, 10, 10, 10),
                Margin = new Thickness(4),
                Content = panel
            };

            var captured = lang;
            btn.Click += (_, _) => L10n.SetLanguage(captured);

            LanguageGrid.Children.Add(btn);
            _languageButtons.Add((lang, btn, check));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();
    private void OnPrintScreenKeyChanged(object? sender, EventArgs e) => ApplyPrintScreenState();

    private void ApplyLanguage()
    {
        Title = L10n.SettingsTitle;
        HeaderTitle.Text = L10n.SettingsTitle;
        ShortcutHeaderText.Text = L10n.SettingsShortcutHeader;
        PrintScreenLabelText.Text = L10n.SettingsPrintScreenLabel;
        PrintScreenDescText.Text = L10n.SettingsPrintScreenDesc;
        LangHeaderText.Text = L10n.SettingsLanguageHeader;
        LangDescText.Text = L10n.SettingsLanguageDesc;
        CloseButton.Content = L10n.SettingsClose;

        foreach (var (lang, button, check) in _languageButtons)
        {
            StyleLanguageButton(button, check, L10n.Current == lang);
        }
    }

    private void ApplyPrintScreenState()
    {
        bool enabled = L10n.UsePrintScreenKey;
        if (enabled)
        {
            var accent = (Brush)FindResource("AccentBrush");
            PrintScreenCheckBox.Background = accent;
            PrintScreenCheckBox.BorderBrush = accent;
            PrintScreenCheckIcon.Visibility = Visibility.Visible;
        }
        else
        {
            PrintScreenCheckBox.Background = Brushes.Transparent;
            PrintScreenCheckBox.BorderBrush = (Brush)FindResource("StrokeBrush");
            PrintScreenCheckIcon.Visibility = Visibility.Collapsed;
        }
    }

    private void PrintScreenToggleButton_Click(object sender, RoutedEventArgs e) =>
        L10n.SetUsePrintScreenKey(!L10n.UsePrintScreenKey);

    private void StyleLanguageButton(Button button, UIElement checkIcon, bool selected)
    {
        if (selected)
        {
            button.Background = (Brush)FindResource("AccentBrush");
            button.BorderBrush = button.Background;
            button.Foreground = Brushes.White;
            checkIcon.Visibility = Visibility.Visible;
        }
        else
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(ForegroundProperty);
            checkIcon.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
