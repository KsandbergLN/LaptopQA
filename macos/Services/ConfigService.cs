using System.Text.Json;
using System.Text.Json.Nodes;

namespace LaptopQA.Mac.Services;

public sealed class ConfigService
{
    private static readonly string[] RetiredConfigKeys =
    {
        "DellWarrantyClientId", "DellWarrantyClientSecretProtected", "DellWarrantyTokenUrl", "DellWarrantyEntitlementsUrl",
        "DellWarrantyTimeoutSeconds", "DellWarrantyCredentialExpirationDate", "IntuneGraphClientId", "IntuneTenantId"
    };
    public string DataRoot { get; }
    public string LocalDataRoot { get; }
    public string QaSheetsFolder => Path.Combine(DataRoot, "QA sheets");
    public string LogsFolder => Path.Combine(DataRoot, "logs");
    public string ActivityFolder => Path.Combine(DataRoot, "activity");
    public string HardwareFolder => Path.Combine(DataRoot, "hardware");
    public string ConfigPath => Path.Combine(DataRoot, "Laptop-QA-Config.json");
    public string LocalConfigPath => Path.Combine(LocalDataRoot, "Laptop-QA-Config.json");
    public string QaSessionCachePath => Path.Combine(DataRoot, ".runtime", "qa-session.json");

    public ConfigService(string? dataRoot = null, string? localDataRoot = null)
    {
        LocalDataRoot = string.IsNullOrWhiteSpace(localDataRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Laptop QA")
            : Path.GetFullPath(localDataRoot);
        DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? LocalDataRoot : Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(LocalDataRoot);
        try { Directory.CreateDirectory(QaSheetsFolder); } catch { }
        try { Directory.CreateDirectory(LogsFolder); } catch { }
        try { Directory.CreateDirectory(ActivityFolder); } catch { }
        try { Directory.CreateDirectory(HardwareFolder); } catch { }
        MigrateActivityLogs();
    }

    public void CleanupManagedOutput(int retentionDays = 90)
    {
        CleanupFolder(QaSheetsFolder, retentionDays, false);
        CleanupFolder(LogsFolder, retentionDays, true);
        CleanupFolder(ActivityFolder, retentionDays, true);
        CleanupFolder(HardwareFolder, retentionDays, false);
        CleanupFolder(Path.Combine(DataRoot, "hash"), retentionDays, false);
    }

    public AppConfig Load()
    {
        try
        {
            var path = File.Exists(ConfigPath) ? ConfigPath : LocalConfigPath;
            var json = File.Exists(path) ? File.ReadAllText(path) : "";
            var config = !string.IsNullOrWhiteSpace(json)
                ? JsonSerializer.Deserialize<AppConfig>(json, JsonOptions()) ?? new AppConfig()
                : new AppConfig();
            NormalizeFinalCheckLinkAssignments(config);
            var hasSavedTheme = !string.IsNullOrWhiteSpace(json) &&
                                JsonNode.Parse(json) is JsonObject saved &&
                                saved.Any(property => property.Key.Equals(nameof(AppConfig.AppTheme), StringComparison.OrdinalIgnoreCase));
            if (!config.ThemePreferenceSet && !hasSavedTheme) config.AppTheme = "Light";
            return config;
        }
        catch
        {
            return new AppConfig();
        }
    }

    private static void NormalizeFinalCheckLinkAssignments(AppConfig config)
    {
        var checkLinkIsDevices = (config.CheckHashAndGroupTagUrl ?? "").Contains("DevicesWindowsMenu", StringComparison.OrdinalIgnoreCase);
        var removeLinkIsEnrollment = (config.RemoveUserFromIntuneUrl ?? "").Contains("AutopilotDevices.ReactView", StringComparison.OrdinalIgnoreCase);
        if (!checkLinkIsDevices || !removeLinkIsEnrollment) return;

        var checkUrl = config.CheckHashAndGroupTagUrl ?? "";
        config.CheckHashAndGroupTagUrl = config.RemoveUserFromIntuneUrl ?? "";
        config.RemoveUserFromIntuneUrl = checkUrl;
    }

    public string? Save(AppConfig config)
    {
        try
        {
            WriteAtomic(LocalConfigPath, JsonSerializer.Serialize(config, JsonOptions()));
        }
        catch (Exception ex)
        {
            return $"The macOS settings could not be saved locally: {ex.Message}";
        }

        if (string.Equals(Path.GetFullPath(LocalConfigPath), Path.GetFullPath(ConfigPath), StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var root = File.Exists(ConfigPath)
                ? JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            var updated = JsonSerializer.SerializeToNode(config, JsonOptions()) as JsonObject ?? new JsonObject();
            foreach (var property in updated)
                root[property.Key] = property.Value?.DeepClone();
            foreach (var retiredKey in RetiredConfigKeys)
                root.Remove(retiredKey);

            WriteAtomic(ConfigPath, root.ToJsonString(JsonOptions()));
            return null;
        }
        catch (Exception ex)
        {
            return $"Settings were saved on this Mac, but the shared Windows configuration could not be updated: {ex.Message}";
        }
    }

    public WindowsQaSessionCache? LoadWindowsCache()
    {
        try
        {
            return File.Exists(QaSessionCachePath)
                ? JsonSerializer.Deserialize<WindowsQaSessionCache>(File.ReadAllText(QaSessionCachePath), JsonOptions())
                : null;
        }
        catch { return null; }
    }

    public void SaveSharedQaEdits(WindowsQaSessionCache cache)
    {
        var runtimeFolder = Path.GetDirectoryName(QaSessionCachePath)!;
        Directory.CreateDirectory(runtimeFolder);

        JsonObject root;
        try
        {
            root = File.Exists(QaSessionCachePath)
                ? JsonNode.Parse(File.ReadAllText(QaSessionCachePath)) as JsonObject ?? new JsonObject()
                : JsonSerializer.SerializeToNode(cache, JsonOptions()) as JsonObject ?? new JsonObject();
        }
        catch
        {
            root = JsonSerializer.SerializeToNode(cache, JsonOptions()) as JsonObject ?? new JsonObject();
        }

        Set(root, nameof(cache.SavedAt), cache.SavedAt);
        Set(root, nameof(cache.FinalHashGroupTag), cache.FinalHashGroupTag);
        Set(root, nameof(cache.FinalCleanedLaptop), cache.FinalCleanedLaptop);
        Set(root, nameof(cache.FinalUpdateStockrooms), cache.FinalUpdateStockrooms);
        Set(root, nameof(cache.FinalTrackpadWorking), cache.FinalTrackpadWorking);
        Set(root, nameof(cache.FinalDeletedUser), cache.FinalDeletedUser);
        Set(root, nameof(cache.FinalConditionSuitableForUse), cache.FinalConditionSuitableForUse);
        Set(root, nameof(cache.TrackpadState), cache.TrackpadState);
        Set(root, nameof(cache.PhysicalConditionState), cache.PhysicalConditionState);
        Set(root, nameof(cache.UsbPortTestFinished), cache.UsbPortTestFinished);
        Set(root, nameof(cache.UsbPorts), cache.UsbPorts);
        Set(root, nameof(cache.RmaIssues), cache.RmaIssues);
        Set(root, nameof(cache.RepairNotes), cache.RepairNotes);
        Set(root, nameof(cache.DiagnosticsLogPath), cache.DiagnosticsLogPath);
        Set(root, nameof(cache.DiagnosticsRawText), cache.DiagnosticsRawText);
        Set(root, nameof(cache.BatteryHealthRating), cache.BatteryHealthRating);

        WriteAtomic(QaSessionCachePath, root.ToJsonString(JsonOptions()));
    }

    public void CleanupMacLocalData()
    {
        var local = Path.GetFullPath(LocalDataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var shared = Path.GetFullPath(DataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(local, shared, StringComparison.OrdinalIgnoreCase)) return;

        var expectedParent = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!local.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(local), "Laptop QA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clean an unexpected local folder: {local}");

        if (Directory.Exists(local)) Directory.Delete(local, true);
    }

    private static void Set<T>(JsonObject root, string propertyName, T value) =>
        root[propertyName] = JsonSerializer.SerializeToNode(value, JsonOptions());

    private static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, contents);
            try
            {
                File.Move(tempPath, path, true);
            }
            catch (IOException)
            {
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private void MigrateActivityLogs()
    {
        try
        {
            Directory.CreateDirectory(ActivityFolder);
            var sources = new List<string>();
            var legacyFolder = Path.Combine(LogsFolder, "activity");
            if (Directory.Exists(legacyFolder)) sources.AddRange(Directory.EnumerateFiles(legacyFolder));
            if (Directory.Exists(LogsFolder)) sources.AddRange(Directory.EnumerateFiles(LogsFolder, "*Activity*", SearchOption.TopDirectoryOnly));
            foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destination = Path.Combine(ActivityFolder, Path.GetFileName(source));
                if (File.Exists(destination))
                    destination = Path.Combine(ActivityFolder, $"{Path.GetFileNameWithoutExtension(source)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{Path.GetExtension(source)}");
                File.Move(source, destination);
            }
            if (Directory.Exists(legacyFolder) && !Directory.EnumerateFileSystemEntries(legacyFolder).Any()) Directory.Delete(legacyFolder);
        }
        catch
        {
            // A legacy migration failure must not interrupt startup.
        }
    }

    private static void CleanupFolder(string folder, int retentionDays, bool recursive)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in Directory.EnumerateFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
                catch
                {
                }
            }

            if (!recursive) return;
            foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
