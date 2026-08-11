using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LaptopQATestingMac;

internal sealed class StartupSplashWindow : Window
{
    public StartupSplashWindow(string joke, string? theme, string? languageCode)
    {
        var normalizedTheme = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase)
            ? "AMOLED"
            : string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        var isLight = normalizedTheme == "Light";
        var isAmoled = normalizedTheme == "AMOLED";
        var shell = Brush.Parse(isLight ? "#F2F5F0" : isAmoled ? "#FF000000" : "#FF20343D");
        var card = Brush.Parse(isLight ? "#FFFFFFFF" : isAmoled ? "#FF080808" : "#FF2A414A");
        var text = Brush.Parse(isLight ? "#FF102A33" : "#FFF3F7F8");
        var muted = Brush.Parse(isLight ? "#FF536970" : "#FFB9C7CB");
        var accent = Brush.Parse(isLight ? "#FF2F7D73" : "#FFA2E6DD");

        Title = "Laptop QA";
        Width = 760;
        Height = 400;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://LaptopQATestingMac/Assets/app-icon.png")));

        var title = new TextBlock
        {
            Text = "Laptop QA",
            Foreground = text,
            FontSize = 34,
            FontWeight = FontWeight.Light,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var status = new TextBlock
        {
            Text = "Preparing the QA workspace…",
            Foreground = muted,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 26),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var jokeText = new TextBlock
        {
            Text = joke,
            Foreground = text,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 590,
            MinHeight = 54,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 420,
            Height = 5,
            Foreground = accent,
            Background = Brush.Parse(isLight ? "#FFDDE7E2" : "#FF415A62"),
            Margin = new Thickness(0, 30, 0, 0)
        };

        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            ClipToBounds = true,
            Background = shell,
            BorderBrush = Brush.Parse(isLight ? "#667F969F" : "#66758A92"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(38),
            Child = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = card,
                Padding = new Thickness(46, 36),
                Child = new StackPanel
                {
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children = { title, status, jokeText, progress }
                }
            }
        };
        AvaloniaLocalization.Apply(this, languageCode);
    }
}
