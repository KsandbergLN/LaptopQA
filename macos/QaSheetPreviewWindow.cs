using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LaptopQA.Mac;

public sealed class QaSheetPreviewWindow : Window
{
    private readonly string _imagePath;
    private readonly Image _image;
    private readonly TextBlock _zoomText;
    private readonly Button _zoomOut;
    private readonly Button _zoomIn;
    private readonly double _baseWidth;
    private readonly string _languageCode;
    private double _zoom = 1;
    private bool _printInProgress;

    public QaSheetPreviewWindow(Window owner, string imagePath, string theme, string languageCode)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("QA sheet PNG was not found.", imagePath);
        _imagePath = imagePath;
        _languageCode = languageCode;
        Owner = owner;
        Title = "QA Sheet";
        Width = 900;
        Height = 860;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        CanResize = true;
        ShowInTaskbar = false;
        Icon = owner.Icon;

        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        var amoled = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
        var shell = Brush.Parse(light ? "#F7FAF8" : amoled ? "#000000" : "#233A44");
        var headerBrush = Brush.Parse(light ? "#EAF1EF" : amoled ? "#111111" : "#2E4B55");
        var text = Brush.Parse(light ? "#102633" : amoled ? "#F4F4F4" : "#F3F7F8");
        var buttonBrush = Brush.Parse(light ? "#D8E4E1" : amoled ? "#303030" : "#3A5964");
        Background = Brushes.Transparent;

        var root = new Grid { RowDefinitions = new RowDefinitions("52,*") };
        var header = new Grid
        {
            Background = headerBrush,
            ColumnDefinitions = new ColumnDefinitions("*,42,62,42,82,30,34")
        };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            BeginMoveDrag(e);
        };
        root.Children.Add(header);

        header.Children.Add(new TextBlock
        {
            Text = "QA Sheet Preview",
            Margin = new Thickness(18, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = text,
            FontSize = 14,
            FontWeight = FontWeight.Bold
        });
        _zoomOut = HeaderButton("−", "Zoom out", text, buttonBrush, 34);
        Grid.SetColumn(_zoomOut, 1); header.Children.Add(_zoomOut);
        _zoomText = new TextBlock { Text = "100%", Foreground = text, FontSize = 11, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_zoomText, 2); header.Children.Add(_zoomText);
        _zoomIn = HeaderButton("+", "Zoom in", text, buttonBrush, 34);
        Grid.SetColumn(_zoomIn, 3); header.Children.Add(_zoomIn);
        var print = HeaderButton("Print", "Print the QA sheet", text, buttonBrush, 70);
        Grid.SetColumn(print, 4); header.Children.Add(print);
        var minimize = MacWindowButton(false, "Minimize QA sheet preview");
        Grid.SetColumn(minimize, 5); header.Children.Add(minimize);
        var close = MacWindowButton(true, "Close QA sheet preview");
        Grid.SetColumn(close, 6); header.Children.Add(close);

        var bitmap = new Bitmap(imagePath);
        _baseWidth = Math.Min(780, bitmap.PixelSize.Width / 2.0);
        _image = new Image
        {
            Source = bitmap,
            Width = _baseWidth,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24)
        };
        var viewer = new ScrollViewer
        {
            Background = shell,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _image
        };
        Grid.SetRow(viewer, 1); root.Children.Add(viewer);
        Content = new Border { Background = shell, BorderBrush = Brush.Parse(light ? "#7F969F" : amoled ? "#4A4A4A" : "#6682949B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = root };

        _zoomOut.Click += (_, _) => SetZoom(_zoom - .1);
        _zoomIn.Click += (_, _) => SetZoom(_zoom + .1);
        print.Click += async (_, _) =>
        {
            if (_printInProgress) return;
            _printInProgress = true;
            print.IsEnabled = false;
            try
            {
                await PrintAsync();
            }
            finally
            {
                _printInProgress = false;
                print.IsEnabled = true;
            }
        };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        close.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SetZoom(1);
        AvaloniaLocalization.Apply(this, _languageCode);
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(Math.Round(value * 10) / 10, .5, 2.5);
        _image.Width = _baseWidth * _zoom;
        _zoomText.Text = $"{_zoom * 100:0}%";
        _zoomOut.IsEnabled = _zoom > .5;
        _zoomIn.IsEnabled = _zoom < 2.5;
    }

    private async Task PrintAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            await NoticeAsync("Printing is available from the packaged macOS app.");
            return;
        }
        if (!await ConfirmAsync("Send this QA sheet to the Mac's default printer?")) return;
        var info = new ProcessStartInfo
        {
            FileName = "/usr/bin/lp",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
		info.ArgumentList.Add("-n"); info.ArgumentList.Add("1");
        info.ArgumentList.Add("-o"); info.ArgumentList.Add("fit-to-page");
        info.ArgumentList.Add("-o"); info.ArgumentList.Add("print-color-mode=color");
        info.ArgumentList.Add("-o"); info.ArgumentList.Add("ColorModel=RGB");
        info.ArgumentList.Add(_imagePath);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The macOS print service could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await NoticeAsync(process.ExitCode == 0 ? (string.IsNullOrWhiteSpace(output) ? "QA sheet sent to the default printer." : output.Trim()) : $"The QA sheet could not be printed. {error.Trim()}");
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var result = false;
        var cancel = new Button { Content = "Cancel", Width = 88, CornerRadius = new CornerRadius(14) };
        var print = new Button { Content = "Print", Width = 88, CornerRadius = new CornerRadius(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, print } };
        var dialog = new Window { Title = "Print QA Sheet", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new StackPanel { Margin = new Thickness(24), Spacing = 20, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } } };
        AvaloniaLocalization.Apply(dialog, _languageCode);
        cancel.Click += (_, _) => dialog.Close(); print.Click += (_, _) => { result = true; dialog.Close(); };
        await dialog.ShowDialog(this);
        return result;
    }

    private async Task NoticeAsync(string message)
    {
        var ok = new Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(14) };
        var dialog = new Window { Title = "Print QA Sheet", Width = 430, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok } } };
        AvaloniaLocalization.Apply(dialog, _languageCode);
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private static Button HeaderButton(string content, string tip, IBrush foreground, IBrush background, double width)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = content,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = width,
            Height = 32,
            Margin = new Thickness(3, 10),
            Padding = new Thickness(0),
            Foreground = foreground,
            Background = background,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button MacWindowButton(bool close, string tip)
    {
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(close
                ? "M1.3,1.3 L6.7,6.7 M6.7,1.3 L1.3,6.7"
                : "M1,4 L7,4"),
            Stroke = Brush.Parse("#85000000"),
            StrokeThickness = close ? 1.25 : 1.3,
            StrokeLineCap = PenLineCap.Round
        };
        var button = new Button
        {
            Content = new Viewbox { Width = 8, Height = 8, Child = glyph },
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 17),
            Background = Brush.Parse(close ? "#FF5F57" : "#FEBC2E"),
            BorderBrush = Brush.Parse("#30000000"),
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        return button;
    }
}
