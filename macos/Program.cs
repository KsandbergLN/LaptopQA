using Avalonia;
using LaptopQA.Shared;
using LaptopQA.Mac.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LaptopQA.Mac;

internal static class Program
{
    private const string DataDriveMarkerFileName = "Laptop-QA-Drive.json";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            RunSelfTest();
            return;
        }

        App.StartupDataRoot = ResolveDataRoot(args, out var removableDataRootDetected);
        App.StartupRemovableDataRootDetected = removableDataRootDetected;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string? ResolveDataRoot(string[] args, out bool removableDataRootDetected)
    {
        var mounted = OperatingSystem.IsMacOS() ? FindPreferredMountedDataRoot("/Volumes") : null;
        removableDataRootDetected = !string.IsNullOrWhiteSpace(mounted);

        var explicitRoot = ReadExplicitDataRoot(args);
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            try
            {
                var configuredRoot = Path.GetFullPath(explicitRoot);
                if (Directory.Exists(configuredRoot)) return configuredRoot;
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(mounted)) return mounted;

        foreach (var start in new[] { Environment.ProcessPath, AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var nearby = FindDataRootNear(start);
            if (!string.IsNullOrWhiteSpace(nearby)) return nearby;
        }

        return null;
    }

    private static string? FindPreferredMountedDataRoot(string volumesRoot)
    {
        try
        {
            if (!Directory.Exists(volumesRoot)) return null;

            return Directory.EnumerateDirectories(volumesRoot)
                .Select(volumeRoot =>
                {
                    try
                    {
                        var dataRoot = FindPackagedDataRoot(volumeRoot);
                        if (string.IsNullOrWhiteSpace(dataRoot)) return null;

                        var hasMarker = File.Exists(Path.Combine(volumeRoot, DataDriveMarkerFileName));
                        var sessionPath = Path.Combine(dataRoot, ".runtime", "qa-session.json");
                        var sessionWriteUtc = File.Exists(sessionPath) ? File.GetLastWriteTimeUtc(sessionPath) : DateTime.MinValue;
                        return new DataRootCandidate(Path.GetFullPath(dataRoot), hasMarker, sessionWriteUtc);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(candidate => candidate is not null)
                .Cast<DataRootCandidate>()
                .OrderByDescending(candidate => candidate.HasMarker)
                .ThenByDescending(candidate => candidate.SessionWriteUtc)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Path)
                .FirstOrDefault();
        }
        catch
        {
            // A protected or disconnected volume should not prevent the app from opening.
            return null;
        }
    }

    private static string? ReadExplicitDataRoot(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--data-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase)) return args[i][12..];
        }
        return null;
    }

    private static string? FindDataRootNear(string? start)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;
        try
        {
            var currentPath = File.Exists(start) ? Path.GetDirectoryName(start) : start;
            var current = string.IsNullOrWhiteSpace(currentPath) ? null : new DirectoryInfo(Path.GetFullPath(currentPath));
            while (current is not null)
            {
                if (IsPackagedDataRoot(current.FullName)) return current.FullName;
                var sibling = FindPackagedDataRoot(current.FullName);
                if (!string.IsNullOrWhiteSpace(sibling)) return sibling;
                current = current.Parent;
            }
        }
        catch
        {
            // Fall through to local application data when a candidate path cannot be inspected.
        }
        return null;
    }

    private static string? FindPackagedDataRoot(string parent)
    {
        if (!Directory.Exists(parent)) return null;
        try
        {
            var exact = Path.Combine(parent, "LAPTOP QA");
            if (IsPackagedDataRoot(exact)) return Path.GetFullPath(exact);
            return Directory.EnumerateDirectories(parent)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "LAPTOP QA", StringComparison.OrdinalIgnoreCase) && IsPackagedDataRoot(path));
        }
        catch { return null; }
    }

    private static bool IsPackagedDataRoot(string path)
    {
        if (!Directory.Exists(path) || !string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), "LAPTOP QA", StringComparison.OrdinalIgnoreCase)) return false;
        return Directory.Exists(Path.Combine(path, ".runtime")) ||
               Directory.Exists(Path.Combine(path, "App")) ||
               File.Exists(Path.Combine(path, "Laptop-QA-Config.json"));
    }

    private sealed record DataRootCandidate(string Path, bool HasMarker, DateTime SessionWriteUtc);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void RunSelfTest()
    {
        if (StartupJokeService.Count != 365)
            throw new InvalidOperationException($"The macOS startup joke catalog contains {StartupJokeService.Count} jokes instead of 365.");
        var jokeTestRoot = Path.Combine(Path.GetTempPath(), "LaptopQA-Mac-JokeSelfTest", Guid.NewGuid().ToString("N"));
        var jokeCycle = Enumerable.Range(0, 365).Select(_ => StartupJokeService.Next(jokeTestRoot)).ToArray();
        if (jokeCycle.Distinct(StringComparer.Ordinal).Count() != 365)
            throw new InvalidOperationException("The macOS startup joke deck repeated before all 365 jokes were shown.");
        var nextCycleJoke = StartupJokeService.Next(jokeTestRoot);
        if (string.Equals(jokeCycle[^1], nextCycleJoke, StringComparison.Ordinal))
            throw new InvalidOperationException("The macOS startup joke deck repeated the prior cycle's last joke immediately.");
        try { Directory.Delete(jokeTestRoot, true); } catch { }

        const string promptLog = "** Video - Functional Test **\nTest Result: Fail\nDIAG07/22/2024 14:57:25Fail ED.3.3.2 Error:2000:0333 Validate code:100639 Video - User provided no input for graphics test\nTest Result: Success";
        var promptResult = DiagnosticsParser.Parse("sample.txt", promptLog);
        if (promptResult.State != "Warning" || !promptResult.UnansweredPrompt || !promptResult.DetailText.Contains("Video", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unanswered diagnostics prompt was not classified as a warning.");

        const string failureLog = "** Memory - Functional Test **\nTest Result: Fail";
        var failureResult = DiagnosticsParser.Parse("sample.txt", failureLog);
        if (failureResult.State != "Bad")
            throw new InvalidOperationException("A real diagnostics failure was not preserved.");

        const string historyLog = "DIAG01/02/2024 10:00:00Fail Error:2000:0123 Validate code:123456 Historical memory error\n** Memory - Functional Test **\nTest Result: Success";
        if (DiagnosticsParser.Parse("sample.txt", historyLog).State != "Ok")
            throw new InvalidOperationException("A historical diagnostics event incorrectly failed the current QA.");

        const string historicalPromptLog = "** Memory - Functional Test **\nTest Result: Success\nDIAG01/02/2024 10:00:00Fail Memory - User provided no input for an older test";
        if (DiagnosticsParser.Parse("sample.txt", historicalPromptLog).State != "Ok")
            throw new InvalidOperationException("A historical unanswered prompt incorrectly warned the current QA.");

        const string retestLog = "** Memory - Functional Test **\nTest Result: Fail\n** Memory - Functional Test **\nTest Result: Success";
        if (DiagnosticsParser.Parse("sample.txt", retestLog).State != "Ok")
            throw new InvalidOperationException("A successful diagnostics retest did not replace the older failure.");

        var cacheRoot = Path.Combine(Path.GetTempPath(), "LaptopQA-Mac-CacheSelfTest", Guid.NewGuid().ToString("N"), "LAPTOP QA");
        var localTestRoot = Path.Combine(Directory.GetParent(cacheRoot)!.FullName, "Local App Data");
        Directory.CreateDirectory(Path.Combine(cacheRoot, ".runtime"));
        var testCache = new WindowsQaSessionCache
        {
            ServiceTag = "TEST123", AssetTag = "7000001", BatterySummary = "Battery Health: Excellent (92%)", BatteryHealthRating = "Poor",
            Hardware = new CachedHardware { Model = "Latitude 7440", OsName = "Microsoft Windows 11 Pro" },
            Steps = new Dictionary<string, CachedQaStep> { ["ExternalVideo"] = new() { State = "Ok", MainText = "External video passed", DetailText = "Cached monitor test passed." } }
        };
        File.WriteAllText(Path.Combine(cacheRoot, ".runtime", "qa-session.json"), JsonSerializer.Serialize(testCache));
        var cacheService = new ConfigService(cacheRoot, localTestRoot);
        Directory.CreateDirectory(cacheService.ActivityFolder);
        var oldLog = Path.Combine(cacheService.ActivityFolder, "old.log");
        var recentLog = Path.Combine(cacheService.LogsFolder, "recent.log");
        var oldHardware = Path.Combine(cacheService.HardwareFolder, "old-hardware.txt");
        File.WriteAllText(oldLog, "old");
        File.WriteAllText(recentLog, "recent");
        File.WriteAllText(oldHardware, "old");
        File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-91));
        File.SetLastWriteTime(oldHardware, DateTime.Now.AddDays(-91));
        cacheService.CleanupManagedOutput(90);
        if (File.Exists(oldLog) || File.Exists(oldHardware) || !File.Exists(recentLog) ||
            !string.Equals(cacheService.LogsFolder, Path.Combine(cacheRoot, "logs"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(cacheService.ActivityFolder, Path.Combine(cacheRoot, "activity"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shared output retention did not keep recent files and remove files older than 90 days.");
        var loadedCache = cacheService.LoadWindowsCache();
        if (loadedCache?.ServiceTag != "TEST123" || loadedCache.Steps["ExternalVideo"].State != "Ok")
            throw new InvalidOperationException("Windows QA session cache was not loaded correctly.");
        var batterySnapshot = typeof(MainWindow).GetMethod("SnapshotFrom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, [loadedCache]) as CachedWindowsSnapshot;
        if (batterySnapshot?.Battery != "Battery Health: Poor (92%)")
            throw new InvalidOperationException("The diagnostics battery rating was not combined with the cached Windows capacity percentage.");
        var parsedRating = typeof(MainWindow).GetMethod("BatteryHealthRatingFromDiagnostics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null,
            ["[ SYSTEM ]\nModel = Latitude\n\n[ BATTERY ]\n    Primary Battery\n        RelativeCharge = 73%\n        Health = Good\n\n[ CHARGER ]\nChargerState = Installed"]) as string;
        if (parsedRating != "Good")
            throw new InvalidOperationException("The Dell diagnostics battery rating was not parsed from the BATTERY section.");

        var originalJson = JsonNode.Parse(File.ReadAllText(cacheService.QaSessionCachePath))!.AsObject();
        originalJson["WindowsOnlySelfTestValue"] = "preserve-me";
        File.WriteAllText(cacheService.QaSessionCachePath, originalJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        loadedCache!.RepairNotes = "Saved on macOS";
        loadedCache.FinalTrackpadWorking = true;
        loadedCache.DiagnosticsLogPath = "/Volumes/IT SUPP/LaptopQA/logs/diagnostics.txt";
        loadedCache.DiagnosticsRawText = "[ BATTERY ]\n    Primary Battery\n        Health = Poor";
        loadedCache.BatteryHealthRating = "Poor";
        cacheService.SaveSharedQaEdits(loadedCache);
        var mergedJson = JsonNode.Parse(File.ReadAllText(cacheService.QaSessionCachePath))!.AsObject();
        if (mergedJson["WindowsOnlySelfTestValue"]?.GetValue<string>() != "preserve-me" ||
            mergedJson[nameof(WindowsQaSessionCache.RepairNotes)]?.GetValue<string>() != "Saved on macOS" ||
            mergedJson[nameof(WindowsQaSessionCache.FinalTrackpadWorking)]?.GetValue<bool>() != true ||
            mergedJson[nameof(WindowsQaSessionCache.BatteryHealthRating)]?.GetValue<string>() != "Poor" ||
            !mergedJson[nameof(WindowsQaSessionCache.DiagnosticsRawText)]!.GetValue<string>().Contains("Health = Poor", StringComparison.Ordinal))
            throw new InvalidOperationException("macOS QA edits were not merged safely into the shared Windows cache.");

        var sharedConfig = new JsonObject
        {
            [nameof(AppConfig.TechnicianName)] = "Windows Technician",
            [nameof(AppConfig.AppTheme)] = "Dark",
            [nameof(AppConfig.DellWarrantyCliPath)] = @"C:\Program Files\Dell\CommandWarranty\DellWarranty-CLI.exe",
            ["DellWarrantyClientId"] = "retired-client-id",
            ["IntuneTenantId"] = "retired-tenant",
            ["WindowsOnlyConfigValue"] = "preserve-me"
        };
        File.WriteAllText(cacheService.ConfigPath, sharedConfig.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var loadedConfig = cacheService.Load();
        if (loadedConfig.TechnicianName != "Windows Technician" || loadedConfig.AppTheme != "Dark")
            throw new InvalidOperationException("macOS did not prefer the shared Windows configuration.");
        loadedConfig.TechnicianName = "macOS Technician";
        loadedConfig.AppTheme = "Light";
        if (cacheService.Save(loadedConfig) is not null)
            throw new InvalidOperationException("macOS reported an unexpected configuration save warning.");
        var mergedConfig = JsonNode.Parse(File.ReadAllText(cacheService.ConfigPath))!.AsObject();
        if (mergedConfig["WindowsOnlyConfigValue"]?.GetValue<string>() != "preserve-me" ||
            mergedConfig[nameof(AppConfig.TechnicianName)]?.GetValue<string>() != "macOS Technician" ||
            mergedConfig[nameof(AppConfig.AppTheme)]?.GetValue<string>() != "Light" ||
            mergedConfig[nameof(AppConfig.DellWarrantyCliPath)]?.GetValue<string>() != @"C:\Program Files\Dell\CommandWarranty\DellWarranty-CLI.exe" ||
            mergedConfig.ContainsKey("DellWarrantyClientId") || mergedConfig.ContainsKey("IntuneTenantId"))
            throw new InvalidOperationException("macOS settings were not merged safely into the shared Windows configuration.");

        var factoryDefaults = new AppConfig();
        if (cacheService.Save(factoryDefaults) is not null)
            throw new InvalidOperationException("Factory settings reported an unexpected configuration save warning.");
        var resetConfig = cacheService.Load();
        var resetJson = JsonNode.Parse(File.ReadAllText(cacheService.ConfigPath))!.AsObject();
        if (!string.IsNullOrWhiteSpace(resetConfig.TechnicianName) || resetConfig.AppTheme != "Light" ||
            resetJson.ContainsKey("DellWarrantyClientId") || resetJson.ContainsKey("IntuneTenantId"))
            throw new InvalidOperationException("Factory settings did not restore defaults and remove retired configuration values.");

        var blockedSharedRoot = Path.Combine(Directory.GetParent(cacheRoot)!.FullName, "Blocked Shared Root");
        File.WriteAllText(blockedSharedRoot, "This file intentionally blocks creation of a shared config folder.");
        var warningService = new ConfigService(blockedSharedRoot, Path.Combine(Directory.GetParent(cacheRoot)!.FullName, "Warning Local Data"));
        var saveWarning = warningService.Save(new AppConfig { TechnicianName = "Local fallback" });
        if (string.IsNullOrWhiteSpace(saveWarning) || !File.Exists(warningService.LocalConfigPath))
            throw new InvalidOperationException("A shared configuration write failure did not fall back to a safe local save warning.");

        var packageRoot = Directory.GetParent(cacheRoot)?.FullName ?? cacheRoot;
        var directAppPath = Path.Combine(packageRoot, "macOS Laptop QA Launcher.app", "Contents", "MacOS");
        Directory.CreateDirectory(directAppPath);
        var discoveredRoot = FindDataRootNear(directAppPath);
        if (!string.Equals(discoveredRoot, cacheRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A directly opened macOS .app did not discover the adjacent Windows QA data root.");

        var volumesRoot = Path.Combine(packageRoot, "Volumes");
        var olderVolume = Path.Combine(volumesRoot, "Older QA Drive");
        var newerVolume = Path.Combine(volumesRoot, "Newer QA Drive");
        var olderDataRoot = Path.Combine(olderVolume, "LAPTOP QA");
        var newerDataRoot = Path.Combine(newerVolume, "LAPTOP QA");
        Directory.CreateDirectory(Path.Combine(olderDataRoot, ".runtime"));
        Directory.CreateDirectory(Path.Combine(newerDataRoot, ".runtime"));
        File.WriteAllText(Path.Combine(olderVolume, DataDriveMarkerFileName), "{}");
        File.WriteAllText(Path.Combine(newerVolume, DataDriveMarkerFileName), "{}");
        var olderSession = Path.Combine(olderDataRoot, ".runtime", "qa-session.json");
        var newerSession = Path.Combine(newerDataRoot, ".runtime", "qa-session.json");
        File.WriteAllText(olderSession, "{}");
        File.WriteAllText(newerSession, "{}");
        File.SetLastWriteTimeUtc(olderSession, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newerSession, DateTime.UtcNow);
        var preferredMountedRoot = FindPreferredMountedDataRoot(volumesRoot);
        if (!string.Equals(preferredMountedRoot, newerDataRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mounted removable-drive discovery did not prefer the newest shared QA session.");

        var output = Path.Combine(Path.GetTempPath(), "LaptopQA-Mac-SelfTest");
        var sheet = QaSheetService.Create(output, new QaSheetData(new AppConfig(), new CachedWindowsSnapshot(), new WindowsQaSessionCache(), promptResult, false, false, false, false, false, false, "", ""));
        var pngHeader = File.Exists(sheet) ? File.ReadAllBytes(sheet).Take(8).ToArray() : [];
        if (!sheet.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || pngHeader.Length != 8 || !pngHeader.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("The self-contained QA sheet PNG was not generated correctly.");

        var serviceNowDescription = ServiceNowService.BuildDescription(new CachedWindowsSnapshot { Model = "Latitude 7440", SerialNumber = "TEST123", AssetTag = "7000001" });
        if (!string.Equals(serviceNowDescription, "Laptop QA | 7440 | TEST123 | 7000001", StringComparison.Ordinal))
            throw new InvalidOperationException("ServiceNow clipboard details were not generated correctly.");

        var topEightLanguages = new[] { "en-US", "es-ES", "fr-FR", "de-DE", "pt-BR", "zh-CN", "ja-JP", "hi-IN" };
        var interfaceSamples = new[] { "Settings", "Save", "Cancel", "Folders", "Start New QA", "Final Checks", "QA Sheet", "Browse", "Search", "Print" };
        foreach (var language in topEightLanguages)
        {
            if (!UiLocalization.HasLanguage(language))
                throw new InvalidOperationException($"The embedded interface translation is missing for {language}.");
            if (language != "en-US" && interfaceSamples.Count(text => UiLocalization.Text(language, text) != text) < 6)
                throw new InvalidOperationException($"The embedded interface translation is incomplete for {language}.");
        }
        if (LanguageCatalog.All.Any(language => !UiLocalization.HasLanguage(language.Code)))
            throw new InvalidOperationException("One or more language-selector entries do not have an embedded interface translation.");
    }
}
