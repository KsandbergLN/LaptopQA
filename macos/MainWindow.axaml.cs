using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaptopQA.Shared;
using LaptopQA.Mac.Services;

namespace LaptopQA.Mac;

public sealed partial class MainWindow : Window
{
    private readonly ConfigService _storage = new(App.StartupDataRoot);
    private AppConfig _config = new();
    private WindowsQaSessionCache _cache = new();
    private CachedWindowsSnapshot _hardware = new();
    private DiagnosticsResult _diagnostics = new("Warning", "Diagnostics cache unavailable", "No cached Windows result.", "", "", false);
    private readonly List<string> _drawerOrder = new();
    private readonly Dictionary<Border, CancellationTokenSource> _drawerAnimations = new();
    private readonly DispatcherTimer _sharedQaSaveTimer;
    private bool _loadingSharedQaSession;
    private bool _sharedQaSessionLoaded;
    private DateTime _sharedQaCacheWriteUtc;
    private DateTime _sharedConfigWriteUtc;
    private bool _closeCleanupComplete;
    private bool _removableDriveWarningShown;
    private bool _completionCelebrated;
    private bool _completionMonitoringEnabled;
    private readonly DateTime _logSessionStarted = DateTime.Now;
    private string _activityLogPath = "";
    private string _errorLogPath = "";
    private readonly HashSet<Button> _tooltipConfiguredButtons = new();
    private const string MacUnavailableMessage = "These features do not work on a Mac.";
    private string T(string text) => UiLocalization.Text(_config.AppLanguage, text);

    public MainWindow()
    {
        InitializeComponent();
        _sharedQaSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _sharedQaSaveTimer.Tick += (_, _) =>
        {
            _sharedQaSaveTimer.Stop();
            SaveSharedQaSession();
        };
        AddHandler(Button.ClickEvent, AnyAppInteraction_Changed, RoutingStrategies.Bubble, handledEventsToo: true);
        RmaIssuesBox.TextChanged += SharedQaInput_Changed;
        RepairNotesBox.TextChanged += SharedQaInput_Changed;
        TrackpadCheck.IsCheckedChanged += SharedQaCheck_Changed;
        HashGroupTagCheck.IsCheckedChanged += SharedQaCheck_Changed;
        RemovedUserCheck.IsCheckedChanged += SharedQaCheck_Changed;
        StockroomsCheck.IsCheckedChanged += SharedQaCheck_Changed;
        CleanedCheck.IsCheckedChanged += SharedQaCheck_Changed;
        ConditionSuitableCheck.IsCheckedChanged += SharedQaCheck_Changed;
        Opened += MainWindow_Opened;
        Activated += MainWindow_Activated;
        Closing += MainWindow_Closing;
    }

    private void ConfigureUnavailableTooltips()
    {
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("unavailable")))
        {
            if (!_tooltipConfiguredButtons.Add(button)) continue;
            ToolTip.SetTip(button, MacUnavailableMessage);
            ToolTip.SetShowDelay(button, 250);
            button.PointerEntered += (_, _) => ToolTip.SetIsOpen(button, true);
            button.PointerExited += (_, _) => ToolTip.SetIsOpen(button, false);
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        ConfigureUnavailableTooltips();
        FitWindowToScreen();
        Activate();
        _config = _storage.Load();
        _storage.CleanupManagedOutput(90);
        if (File.Exists(_storage.ConfigPath)) _sharedConfigWriteUtc = File.GetLastWriteTimeUtc(_storage.ConfigPath);
        LanguageCatalog.ApplyCulture(_config.AppLanguage);
        ApplyTheme(_config.AppTheme);
        AvaloniaLocalization.Apply(this, _config.AppLanguage);
        await PromptForTechnicianNameIfNeededAsync();
        UpdateTechnicianHeader();
        if (Environment.GetCommandLineArgs().Contains("--preview-all-drawers", StringComparer.OrdinalIgnoreCase))
        {
            _drawerOrder.AddRange(["Folders", "Notes", "Activity", "Hardware"]);
            UpdateDrawers();
        }
        var loaded = _storage.LoadWindowsCache();
        if (loaded is null)
        {
            HardwareBox.Text = $"Windows QA cache not found.\nExpected: {_storage.QaSessionCachePath}\n\nNo macOS machine information was collected.";
            AddActivity("Cache", "Windows QA cache was not found. macOS collection remained disabled.");
            await ShowRemovableDriveWarningIfNeededAsync();
            return;
        }

        _loadingSharedQaSession = true;
        _cache = loaded;
        _hardware = SnapshotFrom(_cache);
        UpdateDeviceNameHeader();
        HeaderSerial.Text = T(string.IsNullOrWhiteSpace(_hardware.SerialNumber) ? "Service Tag:" : $"Service Tag: {_hardware.SerialNumber}");
        HeaderAsset.Text = T(string.IsNullOrWhiteSpace(_hardware.AssetTag) ? "Asset:" : $"Asset: {_hardware.AssetTag}");
        HeaderWarranty.Text = T(string.IsNullOrWhiteSpace(_hardware.Warranty) ? "Warranty:" : $"Warranty: {WarrantyDisplayText(_hardware.Warranty)}");
        UpdateBatteryHealthDisplay();
        HardwareBox.Text = _hardware.Summary;
        RmaIssuesBox.Text = _cache.RmaIssues;
        RepairNotesBox.Text = _cache.RepairNotes;
        HashGroupTagCheck.IsChecked = _cache.FinalHashGroupTag == true;
        CleanedCheck.IsChecked = _cache.FinalCleanedLaptop == true;
        StockroomsCheck.IsChecked = _cache.FinalUpdateStockrooms == true;
        TrackpadCheck.IsChecked = _cache.FinalTrackpadWorking == true;
        RemovedUserCheck.IsChecked = _cache.FinalDeletedUser == true;
        ConditionSuitableCheck.IsChecked = _cache.FinalConditionSuitableForUse == true;
        UpdateUsbPortUi();
        _loadingSharedQaSession = false;
        _sharedQaSessionLoaded = true;
        _sharedQaCacheWriteUtc = File.GetLastWriteTimeUtc(_storage.QaSessionCachePath);
        BiosStatusText.Text = string.IsNullOrWhiteSpace(_cache.BiosStatusText) ? "BIOS status unavailable in Windows cache." : _cache.BiosStatusText;

        ShowStep("WiFi", WifiIcon, WifiMain, WifiDetail);
        ShowStep("Ethernet", EthernetIcon, EthernetMain, EthernetDetail);
        ShowStep("Camera", CameraIcon, CameraMain, CameraDetail);
        ShowStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail);
        ShowStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail);
        LoadCachedDiagnostics();
        _completionCelebrated = IsQaComplete();
        _completionMonitoringEnabled = true;
        AddActivity("Cache", $"Loaded Windows QA session cached {_cache.SavedAt:yyyy-MM-dd HH:mm:ss}. No macOS machine information was collected.");
        await ShowRemovableDriveWarningIfNeededAsync();
    }

    private async Task ShowRemovableDriveWarningIfNeededAsync()
    {
        if (App.StartupRemovableDataRootDetected || _removableDriveWarningShown) return;
        _removableDriveWarningShown = true;
        AddActivity("Storage", "No Laptop QA removable drive was detected. The app is using computer-local storage.");
        await ShowNoticeAsync(
            "Removable Drive Not Detected",
            "No Laptop QA removable drive was detected. The app is using storage on this computer.\n\nConnect a drive containing Laptop-QA-Drive.json and the LAPTOP QA folder, then close and reopen the app before continuing QA work.");
    }

    private void SharedQaInput_Changed(object? sender, TextChangedEventArgs e) => ScheduleSharedQaSave();

    private void AnyAppInteraction_Changed(object? sender, RoutedEventArgs e) => ScheduleSharedQaSave();

    private void SharedQaCheck_Changed(object? sender, RoutedEventArgs e)
    {
        ScheduleSharedQaSave();
        CheckForQaCompletionCelebration();
    }

    private bool IsQaComplete()
    {
        static bool IsFinalResult(string? state) => state is "Ok" or "Bad" or "Ignored";
        var requiredTests = new[] { "WiFi", "Ethernet", "Camera", "ExternalVideo", "Keyboard" };
        var testsComplete = requiredTests.All(key =>
            _cache.Steps.TryGetValue(key, out var step) && IsFinalResult(step.State));
        var diagnosticsComplete = IsFinalResult(_diagnostics.State) ||
                                  (_diagnostics.State == "Warning" &&
                                   !_diagnostics.MainText.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
                                   !_diagnostics.MainText.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        var finalChecksComplete = TrackpadCheck.IsChecked == true &&
                                  HashGroupTagCheck.IsChecked == true &&
                                  RemovedUserCheck.IsChecked == true &&
                                  StockroomsCheck.IsChecked == true &&
                                  CleanedCheck.IsChecked == true &&
                                  ConditionSuitableCheck.IsChecked == true;
        var usbPortsComplete = _cache.UsbPorts.Count == 0 ||
                               (_cache.UsbPortTestFinished && _cache.UsbPorts.All(port => port.Passed || port.Failed));
        return testsComplete && diagnosticsComplete && usbPortsComplete && finalChecksComplete;
    }

    private void CheckForQaCompletionCelebration()
    {
        if (!_completionMonitoringEnabled || _completionCelebrated || _loadingSharedQaSession || !_sharedQaSessionLoaded || !IsQaComplete()) return;
        _completionCelebrated = true;
        AddActivity("QA", "All test sections and final checks are complete.");
        PlayQaCompletionCelebration();
    }

    private async void PlayQaCompletionCelebration()
    {
        var overlay = new Canvas
        {
            Width = 1280,
            Height = 720,
            IsHitTestVisible = false,
            ClipToBounds = true,
            Opacity = 1,
            ZIndex = 1000
        };
        Grid.SetRowSpan(overlay, 2);
        Shell.Children.Add(overlay);

        var palette = new[]
        {
            Brush.Parse("#2F855A"),
            Brush.Parse("#4FB6AC"),
            Brush.Parse("#F3C46B"),
            Brush.Parse("#64B5F6"),
            Brush.Parse("#E980B0")
        };
        var random = new Random(23);
        var pieces = new List<(Border Piece, double StartX, double StartY, double Drift, double Delay, double Duration, double StartAngle, double Spin, RotateTransform Rotate)>();

        for (var index = 0; index < 42; index++)
        {
            var rotate = new RotateTransform(random.Next(-30, 31));
            var piece = new Border
            {
                Width = random.Next(6, 12),
                Height = random.Next(10, 18),
                CornerRadius = new CornerRadius(2),
                Background = palette[index % palette.Length],
                Opacity = 0,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = rotate
            };
            var startX = random.Next(35, 1245);
            var startY = random.Next(-90, 50);
            Canvas.SetLeft(piece, startX);
            Canvas.SetTop(piece, startY);
            overlay.Children.Add(piece);
            pieces.Add((
                piece,
                startX,
                startY,
                random.Next(-110, 111),
                random.Next(0, 460) / 1000d,
                random.Next(1450, 2200) / 1000d,
                rotate.Angle,
                random.Next(220, 720),
                rotate));
        }

        var messageScale = new ScaleTransform(0.72, 0.72);
        var message = new Border
        {
            Width = 420,
            Height = 108,
            CornerRadius = new CornerRadius(22),
            Background = Brush.Parse("#F5FFFFFF"),
            BorderBrush = Brush.Parse("#2F855A"),
            BorderThickness = new Thickness(2),
            Opacity = 0,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = messageScale,
            Child = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "QA COMPLETE",
                        Foreground = Brush.Parse("#12313A"),
                        FontSize = 26,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "All sections and final checks are finished.",
                        Foreground = Brush.Parse("#405A63"),
                        FontSize = 12,
                        Margin = new Thickness(0, 5, 0, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    }
                }
            }
        };
        Canvas.SetLeft(message, 430);
        Canvas.SetTop(message, 272);
        overlay.Children.Add(message);

        var started = DateTime.UtcNow;
        const double totalSeconds = 2.58;
        while ((DateTime.UtcNow - started).TotalSeconds < totalSeconds)
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            foreach (var item in pieces)
            {
                var progress = Math.Clamp((elapsed - item.Delay) / item.Duration, 0, 1);
                if (elapsed < item.Delay) continue;
                var eased = progress * progress;
                Canvas.SetTop(item.Piece, item.StartY + (810 - item.StartY) * eased);
                Canvas.SetLeft(item.Piece, item.StartX + item.Drift * progress + Math.Sin(progress * Math.PI * 4) * 12);
                item.Rotate.Angle = item.StartAngle + item.Spin * progress;
                item.Piece.Opacity = progress < 0.78 ? 1 : Math.Max(0, (1 - progress) / 0.22);
            }

            var pop = Math.Clamp(elapsed / 0.42, 0, 1);
            var overshoot = 1 + 0.16 * Math.Sin(pop * Math.PI) * (1 - pop);
            var scale = 0.72 + 0.28 * pop * overshoot;
            messageScale.ScaleX = messageScale.ScaleY = scale;
            message.Opacity = elapsed < 2.25 ? pop : Math.Max(0, (totalSeconds - elapsed) / 0.33);
            overlay.Opacity = elapsed < 2.25 ? 1 : Math.Max(0, (totalSeconds - elapsed) / 0.33);
            await Task.Delay(16);
        }

        Shell.Children.Remove(overlay);
    }

    private void ScheduleSharedQaSave()
    {
        if (_loadingSharedQaSession || !_sharedQaSessionLoaded) return;
        _sharedQaSaveTimer.Stop();
        _sharedQaSaveTimer.Start();
    }

    private void SaveSharedQaSession()
    {
        if (_loadingSharedQaSession) return;
        try
        {
            _cache.SavedAt = DateTime.Now;
            _cache.FinalHashGroupTag = HashGroupTagCheck.IsChecked;
            _cache.FinalCleanedLaptop = CleanedCheck.IsChecked;
            _cache.FinalUpdateStockrooms = StockroomsCheck.IsChecked;
            _cache.FinalTrackpadWorking = TrackpadCheck.IsChecked;
            _cache.FinalDeletedUser = RemovedUserCheck.IsChecked;
            _cache.FinalConditionSuitableForUse = ConditionSuitableCheck.IsChecked;
            _cache.RmaIssues = RmaIssuesBox.Text ?? "";
            _cache.RepairNotes = RepairNotesBox.Text ?? "";
            _storage.SaveSharedQaEdits(_cache);
            _sharedQaSessionLoaded = true;
            _sharedQaCacheWriteUtc = File.GetLastWriteTimeUtc(_storage.QaSessionCachePath);
        }
        catch (Exception ex)
        {
            AddActivity("Cache", $"Shared QA session save failed: {ex.Message}");
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeCleanupComplete) return;
        _sharedQaSaveTimer.Stop();
        try
        {
            SaveSharedQaSession();
            _storage.CleanupMacLocalData();
            _closeCleanupComplete = true;
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            AddActivity("Cleanup", $"Mac-local cleanup failed: {ex.Message}");
            Dispatcher.UIThread.Post(async () => await ShowNoticeAsync("Close Cleanup", $"Laptop QA could not remove its Mac-local files and has remained open.\n\n{ex.Message}"));
        }
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        AvaloniaLocalization.Apply(this, _config.AppLanguage);
        if (File.Exists(_storage.ConfigPath))
        {
            var configWriteUtc = File.GetLastWriteTimeUtc(_storage.ConfigPath);
            if (configWriteUtc > _sharedConfigWriteUtc)
            {
                _config = _storage.Load();
                LanguageCatalog.ApplyCulture(_config.AppLanguage);
                ApplyTheme(_config.AppTheme);
                AvaloniaLocalization.Apply(this, _config.AppLanguage);
                UpdateTechnicianHeader();
                _sharedConfigWriteUtc = configWriteUtc;
                AddActivity("Config", "Reloaded settings changed by the Windows app.");
            }
        }

        if (!_sharedQaSessionLoaded || !File.Exists(_storage.QaSessionCachePath)) return;
        var currentWriteUtc = File.GetLastWriteTimeUtc(_storage.QaSessionCachePath);
        if (currentWriteUtc <= _sharedQaCacheWriteUtc) return;

        var updated = _storage.LoadWindowsCache();
        if (updated is null) return;
        _loadingSharedQaSession = true;
        _cache = updated;
        _hardware = SnapshotFrom(_cache);
        UpdateBatteryHealthDisplay();
        RmaIssuesBox.Text = _cache.RmaIssues;
        RepairNotesBox.Text = _cache.RepairNotes;
        HashGroupTagCheck.IsChecked = _cache.FinalHashGroupTag == true;
        CleanedCheck.IsChecked = _cache.FinalCleanedLaptop == true;
        StockroomsCheck.IsChecked = _cache.FinalUpdateStockrooms == true;
        TrackpadCheck.IsChecked = _cache.FinalTrackpadWorking == true;
        RemovedUserCheck.IsChecked = _cache.FinalDeletedUser == true;
        ConditionSuitableCheck.IsChecked = _cache.FinalConditionSuitableForUse == true;
        UpdateUsbPortUi();
        _loadingSharedQaSession = false;
        _sharedQaCacheWriteUtc = currentWriteUtc;
        AddActivity("Cache", "Reloaded QA notes and final checks changed by the Windows app.");
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            (source is Button or SelectableTextBlock || source.FindAncestorOfType<Button>() is not null || source.FindAncestorOfType<SelectableTextBlock>() is not null)) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void MacCloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void MacMinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MacWindowControl_PointerEntered(object? sender, PointerEventArgs e) => SetMacWindowControlIconsVisible(true);

    private void MacWindowControl_PointerExited(object? sender, PointerEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            SetMacWindowControlIconsVisible(MacMinimizeButton.IsPointerOver || MacCloseButton.IsPointerOver));
    }

    private void SetMacWindowControlIconsVisible(bool visible)
    {
        var opacity = visible ? 1d : 0d;
        MacMinimizeIcon.Opacity = opacity;
        MacCloseIcon.Opacity = opacity;
    }

    private void FitWindowToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        var availableWidth = Math.Max(1, work.Width / screen.Scaling);
        var availableHeight = Math.Max(1, work.Height / screen.Scaling);
        var scale = Math.Max(0.5, Math.Min(availableWidth / 1280.0, availableHeight / 720.0));
        Width = Math.Round(1280 * scale);
        Height = Math.Round(720 * scale);
        var pixelWidth = (int)Math.Round(Width * screen.Scaling);
        var pixelHeight = (int)Math.Round(Height * screen.Scaling);
        Position = new PixelPoint(work.X + ((work.Width - pixelWidth) / 2), work.Y + ((work.Height - pixelHeight) / 2));
    }

    private static CachedWindowsSnapshot SnapshotFrom(WindowsQaSessionCache cache)
    {
        var h = cache.Hardware ?? new CachedHardware();
        var os = string.Join(" ", new[] { h.OsName, h.OsVersion, string.IsNullOrWhiteSpace(h.OsBuild) ? "" : $"build {h.OsBuild}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new CachedWindowsSnapshot
        {
            ComputerName = h.Computer, Manufacturer = h.Manufacturer, Model = h.Model, SerialNumber = cache.ServiceTag,
            AssetTag = cache.AssetTag, Warranty = cache.Warranty, Cpu = h.Cpu,
            Memory = string.IsNullOrWhiteSpace(h.Memory) ? h.PhysicalMemory : h.Memory, Gpu = h.Gpu,
            Storage = h.Storage, OperatingSystem = os, Bios = h.Bios, Battery = BatteryHealthSummary(cache)
        };
    }

    private static string NormalizeBatteryHealthRating(string? value)
    {
        var text = value?.Trim() ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bexcellent\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Excellent";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(good|ok|okay|normal|healthy)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Good";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(fair|warning|warn|degraded)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Fair";
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(poor|bad|critical|failed|failure|replace)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "Poor";
        return "";
    }

    private static string BatteryHealthRatingFromDiagnostics(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var section = System.Text.RegularExpressions.Regex.Match(raw,
            @"^\s*\[\s*BATTERY\s*\]\s*(?<body>.*?)(?=^\s*\[[^\]]+\]|\z)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!section.Success) return "";
        var health = System.Text.RegularExpressions.Regex.Match(section.Groups["body"].Value,
            @"^\s*Health\s*=\s*(?<rating>.+?)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        return health.Success ? NormalizeBatteryHealthRating(health.Groups["rating"].Value) : "";
    }

    private static string BatteryHealthSummary(WindowsQaSessionCache cache)
    {
        var rating = NormalizeBatteryHealthRating(cache.BatteryHealthRating);
        if (string.IsNullOrWhiteSpace(rating)) rating = BatteryHealthRatingFromDiagnostics(cache.DiagnosticsRawText);
        var percentMatch = System.Text.RegularExpressions.Regex.Match(cache.BatterySummary ?? "", @"(?<percent>\d{1,3})\s*%");
        var percentSuffix = percentMatch.Success && int.TryParse(percentMatch.Groups["percent"].Value, out var percent)
            ? $" ({Math.Clamp(percent, 0, 100)}%)"
            : "";
        return $"Battery Health: {(string.IsNullOrWhiteSpace(rating) ? "unavailable" : rating)}{percentSuffix}";
    }

    private void UpdateBatteryHealthDisplay()
    {
        var rating = NormalizeBatteryHealthRating(_cache.BatteryHealthRating);
        if (string.IsNullOrWhiteSpace(rating)) rating = BatteryHealthRatingFromDiagnostics(_cache.DiagnosticsRawText);
        _cache.BatteryHealthRating = rating;
        _hardware.Battery = BatteryHealthSummary(_cache);
        HeaderBattery.Text = T(_hardware.Battery);

        var (filled, color) = rating switch
        {
            "Excellent" => (4, "#22C55E"),
            "Good" => (3, "#EAB308"),
            "Fair" => (2, "#F97316"),
            "Poor" => (1, "#EF4444"),
            _ => (0, "#B9C7CB")
        };
        HeaderBatteryDots.Text = string.Concat(Enumerable.Range(0, 4).Select(index => index < filled ? "\u25CF" : "\u25CB"));
        HeaderBatteryDots.Foreground = Brush.Parse(color);
        var percentMatch = System.Text.RegularExpressions.Regex.Match(_hardware.Battery, @"(?<percent>\d{1,3})\s*%");
        var percentText = percentMatch.Success ? $" Cached Windows capacity health: {percentMatch.Groups["percent"].Value}%." : "";
        var tooltip = string.IsNullOrWhiteSpace(rating)
            ? $"Dell diagnostics log battery rating unavailable.{percentText}"
            : $"Dell diagnostics log battery rating: {rating}.{percentText}";
        ToolTip.SetTip(HeaderBattery, tooltip);
        ToolTip.SetTip(HeaderBatteryDots, tooltip);
    }

    private void ShowStep(string key, TextBlock icon, TextBlock main, TextBlock detail)
    {
        if (!_cache.Steps.TryGetValue(key, out var step)) step = new CachedQaStep { MainText = $"{key} cache unavailable", DetailText = "No cached Windows result." };
        main.Text = step.MainText;
        detail.Text = step.DetailText;
        ApplyState(icon, step.State);
    }

    private void LoadCachedDiagnostics()
    {
        if (!string.IsNullOrWhiteSpace(_cache.DiagnosticsRawText))
            _diagnostics = DiagnosticsParser.Parse(_cache.DiagnosticsLogPath, _cache.DiagnosticsRawText);
        else if (_cache.Steps.TryGetValue("Diagnostics", out var step))
            _diagnostics = new(step.State, step.MainText, step.DetailText, _cache.DiagnosticsLogPath, "", false);

        UpdateDiagnosticsUi();
    }

    private void UpdateDiagnosticsUi()
    {
        DiagnosticsMain.Text = _diagnostics.MainText;
        DiagnosticsDetail.Text = _diagnostics.DetailText;
        ApplyState(DiagnosticsIcon, _diagnostics.State);
        RawLogButton.Classes.Set("unavailable", string.IsNullOrWhiteSpace(_diagnostics.RawText));
    }

    private void UpdateUsbPortUi()
    {
        if (UsbPortIndicatorsPanel is null) return;
        UsbPortIndicatorsPanel.Children.Clear();
        _cache.UsbPorts ??= new List<UsbPortCache>();
        var ports = _cache.UsbPorts;
        var isLight = ActualThemeVariant == ThemeVariant.Light;
        var muted = Brush.Parse(isLight ? "#60757E" : "#B9C7CB");
        var pendingBackground = Brush.Parse(isLight ? "#EAF0EF" : "#241D3038");
        var pendingBorder = Brush.Parse(isLight ? "#8EA4A8" : "#6682949B");
        var pass = Brush.Parse(isLight ? "#2F855A" : "#55E3A4");
        var passBackground = Brush.Parse(isLight ? "#DDEFE5" : "#332F855A");
        var fail = Brush.Parse(isLight ? "#B4232B" : "#FF6B6B");
        var failBackground = Brush.Parse(isLight ? "#F6E0E1" : "#338A4646");

        if (ports.Count == 0)
        {
            UsbPortIndicatorsPanel.Children.Add(CreateUsbPortPromptCard(isLight, pendingBackground, passBackground, pass));
            return;
        }

        const double panelWidth = 348;
        const double panelHeight = 62;
        const double horizontalGap = 4;
        const double verticalGap = 5;
        var columns = Math.Min(6, ports.Count);
        var rows = (int)Math.Ceiling(ports.Count / (double)columns);
        var badgeWidth = Math.Max(36, Math.Floor(panelWidth / columns) - horizontalGap);
        var badgeHeight = Math.Max(15, Math.Floor(panelHeight / rows) - verticalGap);
        var badgeFontSize = ports.Count <= 12 ? 10.5 : 8.5;
        foreach (var port in ports)
        {
            var stateBrush = port.Passed ? pass : port.Failed ? fail : muted;
            var badge = new Border
            {
                Width = badgeWidth,
                Height = badgeHeight,
                Margin = new Thickness(0, 0, horizontalGap, verticalGap),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = port.Passed ? pass : port.Failed ? fail : pendingBorder,
                Background = port.Passed ? passBackground : port.Failed ? failBackground : pendingBackground
            };
            ToolTip.SetTip(badge, port.Passed
                ? $"{port.Label} passed."
                : port.Failed ? $"{port.Label} failed." : $"{port.Label} has not been tested.");
            badge.Child = new TextBlock
            {
                Text = $"{port.Label} {(port.Passed ? "\u2713" : port.Failed ? "\u2715" : "\u2014")}",
                Foreground = stateBrush,
                FontSize = badgeFontSize,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            UsbPortIndicatorsPanel.Children.Add(badge);
        }
    }

    private static Border CreateUsbPortPromptCard(bool isLight, IBrush background, IBrush pillBackground, IBrush accent)
    {
        var card = new Border
        {
            Width = 348,
            Height = 58,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = accent,
            Background = background,
            Padding = new Thickness(12, 7)
        };
        ToolTip.SetTip(card, "Start New QA in Windows, then move a readable USB drive through each port.");

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,62"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        var title = new TextBlock
        {
            Text = "Ready after reset",
            Foreground = Brush.Parse(isLight ? "#06141B" : "#F2F8FA"),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var pill = new Border
        {
            Width = 54,
            CornerRadius = new CornerRadius(9),
            Background = pillBackground,
            Padding = new Thickness(4, 2),
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        pill.Child = new TextBlock
        {
            Text = "Waiting",
            Foreground = Brush.Parse("#102A2D"),
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(pill, 1);
        Grid.SetRow(pill, 0);
        grid.Children.Add(pill);

        var detail = new TextBlock
        {
            Text = "Start New QA in Windows, then move a readable USB drive through each port.",
            Foreground = Brush.Parse(isLight ? "#60757E" : "#B9C7CB"),
            FontSize = 9.6,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetColumn(detail, 0);
        Grid.SetColumnSpan(detail, 2);
        Grid.SetRow(detail, 1);
        grid.Children.Add(detail);

        card.Child = grid;
        return card;
    }

    private static string WarrantyDisplayText(string? warrantyText)
    {
        if (string.IsNullOrWhiteSpace(warrantyText)) return "unavailable X";
        var trimmed = warrantyText.Trim();
        return trimmed + (IsWarrantyCurrent(trimmed) ? " \u2713" : " X");
    }

    private static bool IsWarrantyCurrent(string warrantyText)
    {
        var formats = new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy" };
        return DateTime.TryParseExact(warrantyText.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
               ? date.Date >= DateTime.Today
               : DateTime.TryParse(warrantyText, out date) && date.Date >= DateTime.Today;
    }

    private async void DiagnosticsBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null) throw new InvalidOperationException("The macOS file picker is unavailable.");

            var startFolderPath = DiagnosticsFolderPath();
            var startFolder = Directory.Exists(startFolderPath)
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startFolderPath)
                : null;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select diagnostics log",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter =
                [
                    FilePickerFileTypes.All,
                    new FilePickerFileType("Diagnostics logs") { Patterns = ["*.txt", "*.log", "*.xml", "*.json", "*.csv"] }
                ]
            });
            var selected = files.FirstOrDefault();
            if (selected is null) return;

            var selectedPath = selected.TryGetLocalPath();

            await using var stream = await selected.OpenReadAsync();
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var raw = await reader.ReadToEndAsync();
            _diagnostics = DiagnosticsParser.Parse(selected.TryGetLocalPath() ?? selected.Name, raw);
            _cache.DiagnosticsLogPath = _diagnostics.Path;
            _cache.DiagnosticsRawText = raw;
            _cache.BatteryHealthRating = BatteryHealthRatingFromDiagnostics(raw);
            _hardware = SnapshotFrom(_cache);
            UpdateBatteryHealthDisplay();
            SaveSharedQaSession();
            UpdateDiagnosticsUi();
            CheckForQaCompletionCelebration();
            AddActivity("Diagnostics", $"Loaded and parsed {selected.Name}: {_diagnostics.MainText}.");
        }
        catch (Exception ex)
        {
            AddActivity("Diagnostics", $"Could not load diagnostics log: {ex.Message}");
            await ShowNoticeAsync("Diagnostics", ex.Message);
        }
    }

    private static void ApplyState(TextBlock icon, string state)
    {
        icon.Text = state switch { "Ok" => "\u2713", "Bad" => "\u2715", "Warning" => "\u26A0", "Ignored" => "\u2298", _ => "\u2014" };
        icon.Foreground = Brush.Parse(state switch { "Ok" => "#7FE0A9", "Bad" => "#FF9D9D", "Warning" => "#F3C46B", "Ignored" => "#B9C7CB", _ => "#B9C7CB" });
    }

    private async void RawLogButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_diagnostics.RawText))
        {
            await ShowNoticeAsync("Diagnostics Log", "No diagnostics log is loaded. Use Browse to select a log first.");
            return;
        }
        var search = new TextBox { PlaceholderText = "Search diagnostics log...", Margin = new Thickness(0, 0, 8, 8) };
        var searchLabel = new TextBlock
        {
            Text = "Search log:",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 8)
        };
        ToolTip.SetTip(searchLabel, "Type here to search the diagnostics log.");
        ToolTip.SetTip(search, "Type here to search the diagnostics log.");
        var clear = new Button { Content = "Clear", Classes = { "action" }, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 8) };
        var raw = new TextBox { Text = _diagnostics.RawText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Menlo,Consolas") };
        var status = new TextBlock { Foreground = Brush.Parse("#B9C7CB") };
        void Find()
        {
            var term = search.Text ?? "";
            if (string.IsNullOrWhiteSpace(term)) { raw.SelectionStart = raw.SelectionEnd = 0; status.Text = ""; return; }
            var index = _diagnostics.RawText.IndexOf(term, Math.Max(0, raw.SelectionEnd), StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = _diagnostics.RawText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) { raw.SelectionStart = index; raw.SelectionEnd = index + term.Length; raw.CaretIndex = index + term.Length; status.Text = "Match found"; } else status.Text = "No matches";
        }
        search.TextChanged += (_, _) => { raw.SelectionStart = raw.SelectionEnd = 0; Find(); };
        clear.Click += (_, _) => search.Text = "";
        var panel = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,*"), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        panel.Children.Add(searchLabel);
        Grid.SetColumn(search, 1); panel.Children.Add(search);
        Grid.SetColumn(clear, 2); panel.Children.Add(clear);
        Grid.SetRow(status, 1); Grid.SetColumnSpan(status, 3); panel.Children.Add(status);
        Grid.SetRow(raw, 2); Grid.SetColumnSpan(raw, 3); panel.Children.Add(raw);
        new Window { Title = "Diagnostics Log", Width = 900, Height = 650, Topmost = Topmost, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show(this);
    }

    private async void UnavailableOnMacButton_Click(object? sender, RoutedEventArgs e)
    {
        // These controls remain pointer-aware so their explanatory tooltip works,
        // but clicking them intentionally performs no action on macOS.
        await Task.CompletedTask;
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var originalTheme = _config.AppTheme;
        var dialog = new SettingsWindow(_config, ApplyTheme) { Topmost = Topmost };
        var result = await dialog.ShowDialog<SettingsResult?>(this);
        if (result is null) { ApplyTheme(originalTheme); return; }
        _config = result.Config;
        var saveWarning = _storage.Save(_config);
        LanguageCatalog.ApplyCulture(_config.AppLanguage);
        ApplyTheme(_config.AppTheme);
        AvaloniaLocalization.Apply(this, _config.AppLanguage);
        AddActivity("Config", result.FactoryReset ? "Factory settings restored and technician name removed." : "Configuration saved.");
        if (!string.IsNullOrWhiteSpace(saveWarning))
        {
            AddActivity("Config", saveWarning);
            await ShowNoticeAsync("Config Save Warning", saveWarning);
        }
        if (!result.FactoryReset) await PromptForTechnicianNameIfNeededAsync();
        UpdateTechnicianHeader();
        UpdateDeviceNameHeader();
    }

    private void UpdateTechnicianHeader() =>
        HeaderTechnician.Text = T(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "Technician: not set" : $"Technician: {_config.TechnicianName}");

    private void UpdateDeviceNameHeader()
    {
        _hardware.DeviceName = QaComputerNaming.Resolve(_config, _hardware, _cache);
        HeaderDeviceName.Text = T($"Device Name: {_hardware.DeviceName}");
        ToolTip.SetTip(HeaderDeviceName, "The device name is generated from the format saved in Config.");
        if (HardwareBox is not null) HardwareBox.Text = _hardware.Summary;
    }

    private async Task PromptForTechnicianNameIfNeededAsync()
    {
        if (!string.IsNullOrWhiteSpace(_config.TechnicianName)) return;

        var name = await ShowTechnicianNamePromptAsync();
        if (string.IsNullOrWhiteSpace(name)) return;

        _config.TechnicianName = name.Trim();
        var saveWarning = _storage.Save(_config);
        UpdateTechnicianHeader();
        AddActivity("Onboarding", $"Technician name saved: {_config.TechnicianName}");
        if (!string.IsNullOrWhiteSpace(saveWarning))
        {
            AddActivity("Config", saveWarning);
            await ShowNoticeAsync("Config Save Warning", saveWarning);
        }
    }

    private async Task<string?> ShowTechnicianNamePromptAsync()
    {
        var light = string.Equals(_config.AppTheme, "Light", StringComparison.OrdinalIgnoreCase);
        var amoled = string.Equals(_config.AppTheme, "AMOLED", StringComparison.OrdinalIgnoreCase);
        string Pick(string lightColor, string darkColor, string amoledColor) => light ? lightColor : amoled ? amoledColor : darkColor;

        var background = Brush.Parse(Pick("#FFFDFCF8", "#FF263D46", "#FF080808"));
        var text = Brush.Parse(Pick("#06141B", "#F3F7F8", "#F4F4F4"));
        var muted = Brush.Parse(Pick("#1D323C", "#B9C7CB", "#BDBDBD"));
        var input = Brush.Parse(Pick("#FFFFFFFF", "#FF1D3038", "#FF050505"));
        var stroke = Brush.Parse(Pick("#7F969F", "#82949B", "#5A5A5A"));
        var primary = Brush.Parse(Pick("#2F855A", "#19734A", "#145C3A"));

        var dialog = new Window
        {
            Title = "Laptop QA Onboarding",
            Width = 430,
            Height = 238,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = Topmost,
            Background = background,
            Icon = Icon
        };

        var titleBar = new Grid { Height = 34, Background = background };
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(dialog).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                dialog.BeginMoveDrag(e);
        };
        var title = new TextBlock
        {
            Text = "Laptop QA Onboarding",
            Foreground = muted,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var minimize = CreateMacWindowButton(false, 40, "Minimize onboarding");
        var close = CreateMacWindowButton(true, 12, "Close onboarding");
        minimize.Click += (_, _) => dialog.WindowState = WindowState.Minimized;
        close.Click += (_, _) => dialog.Close(null);
        titleBar.Children.Add(title);
        titleBar.Children.Add(minimize);
        titleBar.Children.Add(close);

        var nameBox = new TextBox
        {
            Height = 34,
            FontSize = 14,
            Background = input,
            Foreground = text,
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            SelectionBrush = Brush.Parse(Pick("#2F6F68", "#A2E6DD", "#666666")),
            SelectionForegroundBrush = Brush.Parse(Pick("#FFFFFF", "#102A2D", "#FFFFFF"))
        };
        var validation = new TextBlock
        {
            Text = "Please enter the technician name.",
            Foreground = Brush.Parse(Pick("#9B3036", "#FF9D9D", "#FF9D9D")),
            FontSize = 11,
            IsVisible = false
        };
        var save = new Button
        {
            Content = "Save",
            Width = 96,
            Height = 34,
            Background = primary,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        void SaveAndClose()
        {
            var value = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                validation.IsVisible = true;
                nameBox.Focus();
                return;
            }
            dialog.Close(value);
        }
        save.Click += (_, _) => SaveAndClose();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveAndClose();
        };
        nameBox.TextChanged += (_, _) => validation.IsVisible = false;

        var body = new Grid
        {
            Margin = new Thickness(28, 8, 28, 22),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,16,Auto")
        };
        var welcome = new TextBlock { Text = "Welcome to Laptop QA", Foreground = text, FontSize = 22, FontWeight = FontWeight.Bold };
        var explanation = new TextBlock
        {
            Text = "Enter the technician name to use on QA sheets and app records.",
            Foreground = muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 12)
        };
        body.Children.Add(welcome);
        Grid.SetRow(explanation, 1); body.Children.Add(explanation);
        Grid.SetRow(nameBox, 2); body.Children.Add(nameBox);
        Grid.SetRow(validation, 3); body.Children.Add(validation);
        Grid.SetRow(save, 4); body.Children.Add(save);

        var root = new Grid { RowDefinitions = new RowDefinitions("34,*") };
        root.Children.Add(titleBar);
        Grid.SetRow(body, 1); root.Children.Add(body);
        dialog.Content = new Border { BorderBrush = stroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = root };
        AvaloniaLocalization.Apply(dialog, _config.AppLanguage);
        dialog.Opened += (_, _) => nameBox.Focus();
        return await dialog.ShowDialog<string?>(this);
    }

    private static Button CreateMacWindowButton(bool close, double rightMargin, string tip)
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
            Margin = new Thickness(0, 0, rightMargin, 0),
            Background = Brush.Parse(close ? "#FF5F57" : "#FEBC2E"),
            BorderBrush = Brush.Parse("#30000000"),
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private void ApplyTheme(string theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        var amoled = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
        string Pick(string lightColor, string darkColor, string amoledColor) => light ? lightColor : amoled ? amoledColor : darkColor;
        Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        Resources["TextBrush"] = Brush.Parse(Pick("#06141B", "#F3F7F8", "#F4F4F4")); Resources["MutedBrush"] = Brush.Parse(Pick("#1D323C", "#B9C7CB", "#BDBDBD"));
        Resources["AccentBrush"] = Brush.Parse(Pick("#004F4A", "#A2E6DD", "#D8D8D8")); Resources["PanelBrush"] = Brush.Parse(Pick("#FFFFFFFF", "#A03A525C", "#F0101010"));
        Resources["PanelStroke"] = Brush.Parse(Pick("#7F969F", "#6682949B", "#5A5A5A")); Resources["InputBrush"] = Brush.Parse(Pick("#FFFFFFFF", "#A01D3038", "#FF080808"));
        Resources["PrimaryBrush"] = Brush.Parse(Pick("#EAF0EF", "#60757E", "#343434")); Resources["ResetBrush"] = Brush.Parse(Pick("#EAF0EF", "#263D46", "#1A1A1A")); Resources["ButtonTextBrush"] = Brush.Parse(Pick("#17313A", "#FFFFFF", "#FFFFFF")); Resources["NeutralButtonBorderBrush"] = Brush.Parse(Pick("#9AAEB0", "#58717A", "#4A4A4A"));
        Resources["DangerBrush"] = Brush.Parse(Pick("#9B3036", "#8A4646", "#4A4A4A")); Resources["PassBrush"] = Brush.Parse(Pick("#2F855A", "#19734A", "#145C3A"));
        Resources["PowerBrush"] = Brush.Parse(Pick("#EEF4F2", "#314852", "#151515")); Resources["TabForegroundBrush"] = Brush.Parse(Pick("#18333D", "#FFFFFF", "#F4F4F4"));
        Resources["FinalCheckBoxBrush"] = Brush.Parse(Pick("#C8DBD7", "#A2E6DD", "#DADADA")); Resources["FinalCheckMarkBrush"] = Brush.Parse(Pick("#12633D", "#102A2D", "#050505"));
        Resources["DrawerBrush"] = Brush.Parse(Pick("#FFFDFCF8", "#FF263D46", "#FF080808")); Resources["ToolTipBrush"] = Brush.Parse(Pick("#F2F6F4", "#314852", "#171717"));
        Resources["SelectionBrush"] = Brush.Parse(Pick("#2F6F68", "#A2E6DD", "#666666")); Resources["SelectionTextBrush"] = Brush.Parse(Pick("#FFFFFF", "#102A2D", "#FFFFFF"));
        Resources["FoldersTabBrush"] = Brush.Parse(Pick("#D1DDD9", "#6B858E", "#303030")); Resources["NotesTabBrush"] = Brush.Parse(Pick("#C5D5D2", "#607982", "#383838"));
        Resources["ActivityTabBrush"] = Brush.Parse(Pick("#B8C8CB", "#526973", "#2E2E2E")); Resources["HardwareTabBrush"] = Brush.Parse(Pick("#AFC1C5", "#49636C", "#242424"));
        Resources["ShellBrush"] = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse(Pick("#FAFAF6", "#253640", "#000000")), 0), new(Color.Parse(Pick("#F0F1EC", "#314A55", "#000000")), .58), new(Color.Parse(Pick("#E3E6E0", "#526A70", "#090909")), 1) } };
        WavePath.Fill = Brush.Parse(Pick("#28A4AFB8", "#3510394A", "#18000000"));
        if (UsbPortIndicatorsPanel is not null) UpdateUsbPortUi();
    }

    private async void QaSheetButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            FinalizePendingUsbPortsForQaSheet();
            SaveSharedQaSession();
            _storage.CleanupManagedOutput(90);
            var path = QaSheetService.Create(_storage.QaSheetsFolder, new QaSheetData(_config, _hardware, _cache, _diagnostics, _cache.FinalHashGroupTag == true, _cache.FinalCleanedLaptop == true, _cache.FinalUpdateStockrooms == true, _cache.FinalTrackpadWorking == true, _cache.FinalDeletedUser == true, _cache.FinalConditionSuitableForUse == true, _cache.RmaIssues ?? "", _cache.RepairNotes ?? ""));
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The QA sheet renderer did not create a PNG file.");
            var viewer = new QaSheetPreviewWindow(this, path, _config.AppTheme, _config.AppLanguage) { Topmost = Topmost };
            await viewer.ShowDialog(this);
            AddActivity("QA Sheet", $"Generated and opened {Path.GetFileName(path)} from cached Windows data.");
        }
        catch (Exception ex) { AddActivity("QA Sheet", $"Generation failed: {ex.Message}"); await ShowNoticeAsync("QA Sheet", ex.Message); }
    }

    private void FinalizePendingUsbPortsForQaSheet()
    {
        _cache.UsbPorts ??= new List<UsbPortCache>();
        if (_cache.UsbPorts.Count == 0 || _cache.UsbPortTestFinished) return;

        var pending = _cache.UsbPorts.Count(port => !port.Passed && !port.Failed);
        foreach (var port in _cache.UsbPorts)
        {
            if (!port.Passed && !port.Failed) port.Failed = true;
        }

        _cache.UsbPortTestFinished = true;
        UpdateUsbPortUi();
        SaveSharedQaSession();
        AddActivity("USB", pending > 0
            ? $"QA Sheet selected with {pending} untested USB port(s). Pending ports were marked failed."
            : "QA Sheet selected. Cached USB port results were finalized.");
        CheckForQaCompletionCelebration();
    }

    private async void ServiceNowButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var description = ServiceNowService.BuildDescription(_hardware);
            await CopyTextAsync(description, "ServiceNow request details copied.");
            Open(_config.ServiceNowRequestUrl);
            AddActivity("ServiceNow", "Request details copied; ServiceNow opened for manual entry.");
            ShowTransientNotification("Request details copied. ServiceNow opened.");
        }
        catch (Exception ex)
        {
            AddActivity("ServiceNow", $"Could not open ServiceNow: {ex.Message}");
            await ShowNoticeAsync("ServiceNow", "The request details were copied, but ServiceNow could not be opened.\n\n" + ex.Message);
        }
    }

    private async void CheckHashGroupTagButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenFinalCheckLinkAsync(_config.CheckHashAndGroupTagUrl, DefaultCheckHashAndGroupTagUrl, "Check Hash and Group Tag", "Intune");

    private async void RemoveUserFromIntuneButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenFinalCheckLinkAsync(_config.RemoveUserFromIntuneUrl, DefaultRemoveUserFromIntuneUrl, "Remove User from Laptop in Intune", "Intune");

    private async void UpdateStockroomsButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenFinalCheckLinkAsync(_config.UpdateStockroomsUrl, DefaultUpdateStockroomsUrl, "Update Stockrooms", "ServiceNow");

    private const string DefaultCheckHashAndGroupTagUrl = "https://intune.microsoft.com/#view/Microsoft_Intune_Enrollment/AutopilotDevices.ReactView/filterOnManualRemediationRequired~/false";
    private const string DefaultRemoveUserFromIntuneUrl = "https://intune.microsoft.com/#view/Microsoft_Intune_DeviceSettings/DevicesWindowsMenu/~/windowsDevices";
    private const string DefaultUpdateStockroomsUrl = "https://reedelsevier.service-now.com/now/nav/ui/classic/params/target/alm_hardware_list.do%3Fsysparm_first_row%3D1%26sysparm_query%3Dserial_number%3D{SERIAL}%26sysparm_query_encoded%3Dserial_number%3D{SERIAL}%26sysparm_view%3D";

    private async Task OpenFinalCheckLinkAsync(string? configuredUrl, string defaultUrl, string actionName, string destination)
    {
        var serial = (_cache.ServiceTag ?? _hardware.SerialNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(serial))
        {
            await ShowNoticeAsync(actionName, "A valid service tag is required before opening " + destination + " for this laptop.");
            return;
        }

        try
        {
            var url = (string.IsNullOrWhiteSpace(configuredUrl) ? defaultUrl : configuredUrl.Trim())
                .Replace("{SERIAL}", Uri.EscapeDataString(serial), StringComparison.Ordinal);
            await CopyTextAsync(serial, actionName + " copied the service tag.");
            Open(url);
            AddActivity(destination, actionName + " opened; service tag copied: " + serial + ".");
            ShowTransientNotification("Service tag copied. " + destination + " opened.");
        }
        catch (Exception ex)
        {
            AddActivity(destination, actionName + " could not open: " + ex.Message);
            await ShowNoticeAsync(actionName, destination + " could not be opened automatically. The service tag is on the clipboard.\n\n" + ex.Message);
        }
    }

    private void FoldersDrawerButton_Click(object? sender, RoutedEventArgs e) => ToggleDrawer("Folders");
    private void NotesDrawerButton_Click(object? sender, RoutedEventArgs e) => ToggleDrawer("Notes");
    private void ActivityDrawerButton_Click(object? sender, RoutedEventArgs e) => ToggleDrawer("Activity");
    private void HardwareDrawerButton_Click(object? sender, RoutedEventArgs e) => ToggleDrawer("Hardware");

    private void ToggleDrawer(string name)
    {
        if (_drawerOrder.Contains(name)) _drawerOrder.Remove(name); else _drawerOrder.Add(name);
        UpdateDrawers();
        AddActivity(name, $"{name} drawer {(_drawerOrder.Contains(name) ? "shown" : "hidden")}.");
    }

    private void CloseDrawerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var panel = button.GetVisualAncestors().OfType<Border>().FirstOrDefault(x => x.Name is "FoldersPanel" or "NotesPanel" or "ActivityPanel" or "HardwarePanel");
        var name = panel?.Name switch { "FoldersPanel" => "Folders", "NotesPanel" => "Notes", "ActivityPanel" => "Activity", "HardwarePanel" => "Hardware", _ => "" };
        if (!string.IsNullOrWhiteSpace(name)) _drawerOrder.Remove(name);
        UpdateDrawers();
    }

    private void UpdateDrawers()
    {
        var panels = new Dictionary<string, Border> { ["Folders"] = FoldersPanel, ["Notes"] = NotesPanel, ["Activity"] = ActivityPanel, ["Hardware"] = HardwarePanel };
        const double panelWidth = 396d;
        const double rightmostOpenLeft = 842d;
        const double leftmostOpenLeft = 54d;
        const double closedRightMargin = -396d;
        var panelStep = _drawerOrder.Count <= 1
            ? panelWidth
            : Math.Min(panelWidth, (rightmostOpenLeft - leftmostOpenLeft) / (_drawerOrder.Count - 1));

        for (var i = 0; i < _drawerOrder.Count; i++)
        {
            var panel = panels[_drawerOrder[i]];
            panel.Width = panelWidth;
            panel.Height = 588;
            panel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            var targetLeft = rightmostOpenLeft - ((_drawerOrder.Count - 1 - i) * panelStep);
            var targetRightMargin = 1280d - targetLeft - panelWidth;
            panel.ZIndex = 20 + i;
            AnimateDrawer(panel, targetRightMargin, false);
        }

        foreach (var pair in panels.Where(pair => !_drawerOrder.Contains(pair.Key)))
        {
            if (pair.Value.IsVisible) AnimateDrawer(pair.Value, closedRightMargin, true);
        }

        SetDrawerTabOpen(FoldersDrawerTab, _drawerOrder.Contains("Folders"));
        SetDrawerTabOpen(NotesDrawerTab, _drawerOrder.Contains("Notes"));
        SetDrawerTabOpen(ActivityDrawerTab, _drawerOrder.Contains("Activity"));
        SetDrawerTabOpen(HardwareDrawerTab, _drawerOrder.Contains("Hardware"));
    }

    private void SetDrawerTabOpen(Button tab, bool isOpen)
    {
        tab.BorderThickness = isOpen ? new Thickness(2.4) : new Thickness(0);
        tab.BorderBrush = isOpen
            ? Brush.Parse(_config.AppTheme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "#5F9EA8"
                : _config.AppTheme.Equals("AMOLED", StringComparison.OrdinalIgnoreCase) ? "#D0D0D0" : "#8FB8C1")
            : Brushes.Transparent;
    }

    private void AnimateDrawer(Border panel, double targetRightMargin, bool hideAfter)
    {
        if (_drawerAnimations.Remove(panel, out var previous)) previous.Cancel();
        var cancellation = new CancellationTokenSource();
        _drawerAnimations[panel] = cancellation;

        var fromRightMargin = panel.Margin.Right;
        if (!panel.IsVisible)
        {
            fromRightMargin = -396;
            panel.Margin = new Thickness(0, 0, fromRightMargin, 0);
            panel.IsVisible = true;
        }

        _ = AnimateDrawerAsync(panel, fromRightMargin, targetRightMargin, hideAfter, cancellation);
    }

    private async Task AnimateDrawerAsync(Border panel, double fromRightMargin, double targetRightMargin, bool hideAfter, CancellationTokenSource cancellation)
    {
        try
        {
            const int frames = 15;
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var progress = frame / (double)frames;
                var eased = 1 - Math.Pow(1 - progress, 3);
                var rightMargin = fromRightMargin + ((targetRightMargin - fromRightMargin) * eased);
                panel.Margin = new Thickness(0, 0, rightMargin, 0);
                await Task.Delay(16, cancellation.Token);
            }

            panel.Margin = new Thickness(0, 0, targetRightMargin, 0);
            if (hideAfter) panel.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_drawerAnimations.TryGetValue(panel, out var current) && ReferenceEquals(current, cancellation))
                _drawerAnimations.Remove(panel);
            cancellation.Dispose();
        }
    }

    private async void ActivityCopyButton_Click(object? sender, RoutedEventArgs e) => await CopyTextAsync(ActivityBox.Text ?? "", "Activity log copied.");

    private async Task CopyTextAsync(string value, string activity)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(value);
        AddActivity("Clipboard", activity);
    }

    private async void ActivitySaveButton_Click(object? sender, RoutedEventArgs e)
    {
        _storage.CleanupManagedOutput(90);
        EnsureSessionLogPaths();
        AddActivity("Activity", "Activity log is already being saved automatically for this app session.");
        await ShowNoticeAsync("Activity", $"Activity is saved automatically in the single log for this app session:\n{_activityLogPath}");
        Open(_storage.ActivityFolder);
    }

    private void OpenQaSheetsFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(_storage.QaSheetsFolder, "QA Sheets");
    private void OpenLogsFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(_storage.LogsFolder, "Logs");
    private void OpenActivityFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(_storage.ActivityFolder, "Activity");
    private void OpenHashFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_storage.DataRoot, "hash"), "Hash");
    private void OpenHardwareFolderButton_Click(object? sender, RoutedEventArgs e) => OpenFolder(Path.Combine(_storage.DataRoot, "hardware"), "Hardware");
    private async void OpenCameraRollFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = ExpandHome(_config.CameraRoll);
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            OpenFolder(path, "Camera Roll");
            return;
        }

        AddActivity("Folders", $"Camera Roll folder is unavailable on this Mac: {_config.CameraRoll}");
        await ShowNoticeAsync("Camera Roll", $"The Camera Roll location saved in Config is not available on this Mac.\n\nSaved location: {_config.CameraRoll}");
    }

    private void OpenDiagnosticsFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = DiagnosticsFolderPath();
        if (Directory.Exists(path))
        {
            OpenFolder(path, "Diagnostics");
            return;
        }

        _ = ShowNoticeAsync("Diagnostics", "No FAT32 diagnostics drive was detected. Connect the diagnostics drive and try again.");
    }

    private string DiagnosticsFolderPath()
    {
        return FindFat32DiagnosticsVolume();
    }

    private static bool IsPathInsideFolder(string path, string folder)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullFolder, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string FindFat32DiagnosticsVolume()
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists("/Volumes")) return "";

        try
        {
            var mountedDrives = DriveInfo.GetDrives()
                .Where(drive =>
                {
                    try
                    {
                        return drive.IsReady &&
                               drive.RootDirectory.FullName.StartsWith("/Volumes/", StringComparison.Ordinal) &&
                               IsFat32Format(drive.DriveFormat);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Select(drive => drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar))
                .Where(Directory.Exists)
                .OrderByDescending(path => File.Exists(Path.Combine(path, "DellPrebootDiagnosticsLog.txt")))
                .ThenByDescending(path => Path.GetFileName(path).Contains("DELL DIAG", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var detected = mountedDrives.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(detected)) return detected;

            // Some macOS USB mounts are not surfaced through .NET DriveInfo.
            // The Dell log name at a mounted volume root is a stronger fallback signal.
            return Directory.EnumerateDirectories("/Volumes")
                .Where(path => File.Exists(Path.Combine(path, "DellPrebootDiagnosticsLog.txt")))
                .OrderByDescending(path => Path.GetFileName(path).Contains("DELL DIAG", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsFat32Format(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        var normalized = format.Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized is "fat32" or "msdos" or "msdosfs" or "vfat";
    }

    private static string ExpandHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var trimmed = path.Trim();
        if (trimmed == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (trimmed.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), trimmed[2..]);
        return trimmed;
    }

    private void OpenFolder(string path, string label)
    {
        if (!Directory.Exists(path)) { AddActivity("Folders", $"{label} folder is not present in the Windows package cache."); return; }
        Open(path); AddActivity("Folders", $"{label} folder opened.");
    }

    private void AddActivity(string section, string message)
    {
        var now = DateTime.Now;
        ActivityBox.Text += $"[{now:HH:mm:ss}] [{section}] {message}{Environment.NewLine}";
        try
        {
            EnsureSessionLogPaths();
            File.AppendAllText(_activityLogPath, $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{section}] {message}{Environment.NewLine}");
            if (System.Text.RegularExpressions.Regex.IsMatch(message,
                    @"\b(failed|failure|error|exception|timed out|access denied|denied|not accepted|not supported|not found|not loaded|not verified|unavailable|could not)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                File.AppendAllText(_errorLogPath,
                    $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{section}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never interrupt the QA workflow.
        }

        if (!section.Equals("Cache", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleSharedQaSave();
        }
    }

    private void EnsureSessionLogPaths()
    {
        if (!string.IsNullOrWhiteSpace(_activityLogPath)) return;
        Directory.CreateDirectory(_storage.LogsFolder);
        Directory.CreateDirectory(_storage.ActivityFolder);
        var prefix = $"{OutputComputerName()}-{_logSessionStarted:yyyyMMdd-HHmmss-fff}";
        _activityLogPath = Path.Combine(_storage.ActivityFolder, $"{prefix}-Activity.log");
        _errorLogPath = Path.Combine(_storage.LogsFolder, $"{prefix}-Errors.log");
    }

    private string OutputComputerName()
    {
        return SafeOutputFilePart(QaComputerNaming.Resolve(_config, _hardware), "Laptop");
    }

    private static string SafeOutputFilePart(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(source.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
    private static void Open(string target)
    {
        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo { FileName = "/usr/bin/open", UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add(target);
            if (Process.Start(info) is null) throw new InvalidOperationException("macOS could not open the requested item.");
            return;
        }
        if (Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }) is null)
            throw new InvalidOperationException("The requested item could not be opened.");
    }
    private async Task ShowNoticeAsync(string title, string message)
    {
        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Width = 80, CornerRadius = new CornerRadius(14) };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 18, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok } };
        var window = new Window { Title = title, Width = 440, Topmost = Topmost, SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        AvaloniaLocalization.Apply(window, _config.AppLanguage);
        ok.Click += (_, _) => window.Close(); await window.ShowDialog(this);
    }

    private void ShowTransientNotification(string message)
    {
        var light = string.Equals(_config.AppTheme, "Light", StringComparison.OrdinalIgnoreCase);
        var amoled = string.Equals(_config.AppTheme, "AMOLED", StringComparison.OrdinalIgnoreCase);
        var background = Brush.Parse(light ? "#FFF8FAF9" : amoled ? "#FF101010" : "#FF263D46");
        var border = Brush.Parse(light ? "#FF9DB3B9" : amoled ? "#FF535353" : "#FF65828B");
        var foreground = Brush.Parse(light ? "#FF13252D" : "#FFF3F7F8");
        var accent = Brush.Parse(light ? "#FF12633D" : amoled ? "#FFB0B0B0" : "#FF7DCDBE");
        var text = new TextBlock { Text = message, Foreground = foreground, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("9,*"), Children = { new Border { Background = accent, CornerRadius = new CornerRadius(4) }, text } };
        Grid.SetColumn(text, 1);
        var window = new Window
        {
            Width = 350,
            Height = 68,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = Topmost,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border { Background = background, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12), BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 4, Color = Color.Parse(amoled ? "#99000000" : "#66000000") }), Child = content }
        };
        window.Opened += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            window.Close();
        };
        window.Show(this);
    }
}
