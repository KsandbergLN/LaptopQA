using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace LaptopQATestingMac;

public sealed record SettingsResult(AppConfig Config, bool FactoryReset);

public sealed partial class SettingsWindow : Window
{
    private AppConfig _config;
    private readonly Action<string>? _previewTheme;

    public SettingsWindow() : this(new AppConfig(), null) { }

    public SettingsWindow(AppConfig config, Action<string>? previewTheme = null)
    {
        _previewTheme = previewTheme;
        _config = Clone(config);
        InitializeComponent();
        Populate(_config);
        ApplySettingsTheme(NormalizeTheme(_config.AppTheme));
        ThemeBox.SelectionChanged += ThemeBox_SelectionChanged;
        LanguageBox.SelectionChanged += LanguageBox_SelectionChanged;
        AvaloniaLocalization.Apply(this, _config.AppLanguage);
    }

    private void Populate(AppConfig c)
    {
        TechnicianNameBox.Text = c.TechnicianName;
        LanguageBox.ItemsSource = LanguageCatalog.All;
        LanguageBox.SelectedItem = LanguageCatalog.Resolve(c.AppLanguage);
        ThemeBox.SelectedIndex = NormalizeTheme(c.AppTheme) switch { "Light" => 1, "AMOLED" => 2, _ => 0 };
        AutopilotGroupTagBox.Text = string.IsNullOrWhiteSpace(c.AutopilotGroupTag) ? "LNG AAD" : c.AutopilotGroupTag;
        QaComputerNameFormatBox.Text = string.IsNullOrWhiteSpace(c.QaComputerNameFormat) ? "LNG-{serial}" : c.QaComputerNameFormat;
        DiagnosticsFolderBox.Text = c.DellDiagnosticsLogFolder;
        CameraRollBox.Text = c.CameraRoll;
        CleanupTimeoutBox.Text = c.CameraRollCleanupTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        CleanupRetryBox.Text = c.CameraRollCleanupRetryDelaySeconds.ToString(CultureInfo.InvariantCulture);
        WifiDelayBox.Text = c.WifiRescanEthernetDisableDelaySeconds.ToString(CultureInfo.InvariantCulture);
        EthernetDelayBox.Text = c.EthernetRestoreDelaySeconds.ToString(CultureInfo.InvariantCulture);
        ServiceNowUrlBox.Text = c.ServiceNowRequestUrl;
        ServiceNowTypeBox.Text = c.ServiceNowTypeOfRequest;
        ServiceNowDelayBox.Text = c.ServiceNowAutomationDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        ServiceNowGroupBox.Text = c.ServiceNowAssignmentGroupName;
        ServiceNowGroupIdBox.Text = c.ServiceNowAssignmentGroupSysId;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryInt(CleanupTimeoutBox, "Camera cleanup timeout", out var cleanup) ||
            !TryInt(CleanupRetryBox, "Cleanup retry delay", out var retry) ||
            !TryInt(WifiDelayBox, "Wi-Fi rescan wait", out var wifi) ||
            !TryInt(EthernetDelayBox, "Ethernet restore wait", out var ethernet) ||
            !TryInt(ServiceNowDelayBox, "ServiceNow wait", out var serviceNow)) return;

        _config.TechnicianName = TechnicianNameBox.Text?.Trim() ?? "";
        _config.AppTheme = SelectedTheme();
        _config.AppLanguage = (LanguageBox.SelectedItem as AppLanguage)?.Code ?? "en-US";
        _config.ThemePreferenceSet = true;
        _config.AutopilotGroupTag = string.IsNullOrWhiteSpace(AutopilotGroupTagBox.Text) ? "LNG AAD" : AutopilotGroupTagBox.Text.Trim();
        _config.QaComputerNameFormat = string.IsNullOrWhiteSpace(QaComputerNameFormatBox.Text) ? "LNG-{serial}" : QaComputerNameFormatBox.Text.Trim();
        _config.DellDiagnosticsLogFolder = DiagnosticsFolderBox.Text?.Trim() ?? "";
        _config.CameraRoll = CameraRollBox.Text?.Trim() ?? "";
        _config.CameraRollCleanupTimeoutSeconds = cleanup;
        _config.CameraRollCleanupRetryDelaySeconds = retry;
        _config.WifiRescanEthernetDisableDelaySeconds = wifi;
        _config.EthernetRestoreDelaySeconds = ethernet;
        _config.ServiceNowRequestUrl = ServiceNowUrlBox.Text?.Trim() ?? "";
        _config.ServiceNowTypeOfRequest = ServiceNowTypeBox.Text?.Trim() ?? "Other";
        _config.ServiceNowAutomationDelayMilliseconds = serviceNow;
        _config.ServiceNowAssignmentGroupName = ServiceNowGroupBox.Text?.Trim() ?? "";
        _config.ServiceNowAssignmentGroupSysId = ServiceNowGroupIdBox.Text?.Trim() ?? "";
        Close(new SettingsResult(_config, false));
        await Task.CompletedTask;
    }

    private async void FactorySettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync()) return;
        _config = new AppConfig();
        Close(new SettingsResult(_config, true));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private async void BrowseCameraRollButton_Click(object? sender, RoutedEventArgs e) =>
        await ChooseFolderAsync(CameraRollBox, "Choose Camera Roll folder");

    private async void BrowseDiagnosticsFolderButton_Click(object? sender, RoutedEventArgs e) =>
        await ChooseFolderAsync(DiagnosticsFolderBox, "Choose Dell diagnostics log folder");

    private async Task ChooseFolderAsync(TextBox target, string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        if (folders.Count > 0) target.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private void ThemeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var theme = SelectedTheme();
        ApplySettingsTheme(theme);
        _previewTheme?.Invoke(theme);
    }

    private void LanguageBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _config.AppLanguage = (LanguageBox.SelectedItem as AppLanguage)?.Code ?? "en-US";
        LanguageCatalog.ApplyCulture(_config.AppLanguage);
        AvaloniaLocalization.Apply(this, _config.AppLanguage);
    }

    private string SelectedTheme() => ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "AMOLED", _ => "Dark" };

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        if (string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase)) return "AMOLED";
        return "Dark";
    }

    private void ApplySettingsTheme(string theme)
    {
        var light = theme == "Light";
        var amoled = theme == "AMOLED";
        Resources["ConfigTextBrush"] = Brush.Parse(light ? "#06141B" : amoled ? "#F4F4F4" : "#F3F7F8");
        Resources["ConfigMutedBrush"] = Brush.Parse(light ? "#1D323C" : amoled ? "#BDBDBD" : "#B9C7CB");
        Resources["ConfigAccentBrush"] = Brush.Parse(light ? "#004F4A" : amoled ? "#D8D8D8" : "#A2E6DD");
        Resources["ConfigShellBrush"] = Brush.Parse(light ? "#FAFAF6" : amoled ? "#000000" : "#253640");
        Resources["ConfigInputBrush"] = Brush.Parse(light ? "#FFFFFF" : amoled ? "#080808" : "#1D3038");
        Resources["ConfigStrokeBrush"] = Brush.Parse(light ? "#7F969F" : amoled ? "#5A5A5A" : "#6682949B");
        Resources["ConfigSelectionBrush"] = Brush.Parse(light ? "#2F6F68" : amoled ? "#666666" : "#A2E6DD");
        Resources["ConfigSelectionTextBrush"] = Brush.Parse(light ? "#FFFFFF" : amoled ? "#FFFFFF" : "#102A2D");
        Resources["ConfigToolTipBrush"] = Brush.Parse(light ? "#F2F6F4" : amoled ? "#171717" : "#314852");
        Resources["ConfigButtonBrush"] = Brush.Parse(light ? "#D8E1DF" : amoled ? "#303030" : "#485D66");
        Resources["ConfigButtonHoverBrush"] = Brush.Parse(light ? "#C5D5D2" : amoled ? "#484848" : "#5A717A");
        Resources["ConfigButtonPressedBrush"] = Brush.Parse(light ? "#AFC4C0" : amoled ? "#222222" : "#354A53");
        Resources["ConfigDangerBrush"] = Brush.Parse(light ? "#9B3036" : amoled ? "#4A4A4A" : "#8A4646");
        Resources["ConfigDangerHoverBrush"] = Brush.Parse(light ? "#BA4147" : amoled ? "#686868" : "#A75A5A");
        Resources["ConfigDangerPressedBrush"] = Brush.Parse(light ? "#782229" : amoled ? "#333333" : "#6D3535");
        Resources["ConfigSaveBrush"] = Brush.Parse(light ? "#2F855A" : amoled ? "#145C3A" : "#19734A");
        Resources["ConfigSaveHoverBrush"] = Brush.Parse(light ? "#3EA96F" : amoled ? "#1F7B4F" : "#248F5F");
        Resources["ConfigSavePressedBrush"] = Brush.Parse(light ? "#246A48" : amoled ? "#0D4229" : "#115A39");
    }

    private bool TryInt(TextBox box, string label, out int value)
    {
        if (int.TryParse(box.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0) return true;
        box.Focus();
        Title = $"Config — {label} must be a whole number";
        return false;
    }

    private async Task<bool> ConfirmAsync()
    {
        var result = false;
        var theme = SelectedTheme();
        var light = theme == "Light";
        var amoled = theme == "AMOLED";
        var shell = Brush.Parse(light ? "#FAFAF6" : amoled ? "#000000" : "#253640");
        var text = Brush.Parse(light ? "#06141B" : amoled ? "#F4F4F4" : "#F3F7F8");
        var danger = Brush.Parse(light ? "#9B3036" : amoled ? "#4A4A4A" : "#8A4646");
        var dangerHover = Brush.Parse(light ? "#BA4147" : amoled ? "#686868" : "#A75A5A");
        var dangerPressed = Brush.Parse(light ? "#782229" : amoled ? "#333333" : "#6D3535");
        var standard = Brush.Parse(light ? "#D8E1DF" : amoled ? "#303030" : "#485D66");
        var standardHover = Brush.Parse(light ? "#C5D5D2" : amoled ? "#484848" : "#5A717A");
        var standardPressed = Brush.Parse(light ? "#AFC4C0" : amoled ? "#222222" : "#354A53");
        var yes = new Button { Content = "Restore", Foreground = Brushes.White, Width = 92, CornerRadius = new CornerRadius(14) };
        var no = new Button { Content = "Cancel", Foreground = text, Width = 92, CornerRadius = new CornerRadius(14) };
        AddButtonHighlight(yes, danger, dangerHover, dangerPressed);
        AddButtonHighlight(no, standard, standardHover, standardPressed);
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 10, Children = { no, yes } };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 20, Children = { new TextBlock { Text = "Reset every configuration setting to its default and remove the saved technician name?\n\nSaved QA sheets, logs, and hardware files will not be deleted.", Foreground = text, TextWrapping = TextWrapping.Wrap }, buttons } };
        var dialog = new Window { Title = "Reset Settings", Width = 480, Topmost = Topmost, Background = shell, SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return result;
    }

    private static void AddButtonHighlight(Button button, IBrush normal, IBrush hover, IBrush pressed)
    {
        button.Background = normal;
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = normal;
        button.PointerPressed += (_, _) => button.Background = pressed;
        button.PointerReleased += (_, _) => button.Background = button.IsPointerOver ? hover : normal;
    }

    private static AppConfig Clone(AppConfig c) => new()
    {
        TechnicianName = c.TechnicianName, AppTheme = c.AppTheme, AppLanguage = c.AppLanguage, ThemePreferenceSet = c.ThemePreferenceSet, CameraRoll = c.CameraRoll, DellDiagnosticsLogFolder = c.DellDiagnosticsLogFolder, DellWarrantyCliPath = c.DellWarrantyCliPath, AutopilotGroupTag = c.AutopilotGroupTag, QaComputerNameFormat = c.QaComputerNameFormat,
        CameraRollCleanupTimeoutSeconds = c.CameraRollCleanupTimeoutSeconds, CameraRollCleanupRetryDelaySeconds = c.CameraRollCleanupRetryDelaySeconds,
        WifiRescanEthernetDisableDelaySeconds = c.WifiRescanEthernetDisableDelaySeconds, EthernetRestoreDelaySeconds = c.EthernetRestoreDelaySeconds,
        ServiceNowRequestUrl = c.ServiceNowRequestUrl,
        ServiceNowTypeOfRequest = c.ServiceNowTypeOfRequest, ServiceNowAssignmentGroupName = c.ServiceNowAssignmentGroupName,
        ServiceNowAssignmentGroupSysId = c.ServiceNowAssignmentGroupSysId, ServiceNowAutomationDelayMilliseconds = c.ServiceNowAutomationDelayMilliseconds
    };
}
