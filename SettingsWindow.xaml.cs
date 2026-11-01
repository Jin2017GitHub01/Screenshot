using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Screenshot;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.ApplyDarkTitleBar(this);
        Closed += (_, _) => L10n.LanguageChanged -= OnLanguageChanged;
        L10n.LanguageChanged += OnLanguageChanged;
        ApplyLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        Title = L10n.SettingsTitle;
        HeaderTitle.Text = L10n.SettingsTitle;
        LangHeaderText.Text = L10n.SettingsLanguageHeader;
        LangDescText.Text = L10n.SettingsLanguageDesc;
        CloseButton.Content = L10n.SettingsClose;

        bool isKo = L10n.Current == AppLanguage.Korean;
        StyleLanguageButton(EnglishButton, EnglishCheck, !isKo);
        StyleLanguageButton(KoreanButton, KoreanCheck, isKo);
    }

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

    private void EnglishButton_Click(object sender, RoutedEventArgs e) =>
        L10n.SetLanguage(AppLanguage.English);

    private void KoreanButton_Click(object sender, RoutedEventArgs e) =>
        L10n.SetLanguage(AppLanguage.Korean);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}

