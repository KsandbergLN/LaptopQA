using System.Text.Json.Serialization;

namespace LaptopQA.Mac;

public sealed class AppConfig
{
    public string TechnicianName { get; set; } = "";
    public string AppTheme { get; set; } = "Light";
    public string AppLanguage { get; set; } = "en-US";
    [JsonIgnore]
    public bool ThemePreferenceSet { get; set; }
    public string CameraRoll { get; set; } = @"C:\Users\defaultuser0\Pictures\Camera Roll";
    public string DellDiagnosticsLogFolder { get; set; } = "";
    public int CameraRollCleanupTimeoutSeconds { get; set; } = 30;
    public int CameraRollCleanupRetryDelaySeconds { get; set; } = 2;
    public int WifiRescanEthernetDisableDelaySeconds { get; set; } = 3;
    public int EthernetRestoreDelaySeconds { get; set; } = 2;
    public string DellWarrantyCliPath { get; set; } = "";
    public string AutopilotGroupTag { get; set; } = "LNG AAD";
    public string QaComputerNameFormat { get; set; } = "LNG-{serial}";
    public string ServiceNowRequestUrl { get; set; } = "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982";
    public string ServiceNowTypeOfRequest { get; set; } = "Other";
    public string ServiceNowAssignmentGroupName { get; set; } = "Desktop Support (Miamisburg) - L2";
    public string ServiceNowAssignmentGroupSysId { get; set; } = "9d144e37bdef1000e25cbf141e60d715";
    public int ServiceNowAutomationDelayMilliseconds { get; set; } = 500;
    public string CheckHashAndGroupTagUrl { get; set; } = "https://intune.microsoft.com/#view/Microsoft_Intune_DeviceSettings/DevicesWindowsMenu/~/windowsDevices";
    public string RemoveUserFromIntuneUrl { get; set; } = "https://intune.microsoft.com/#view/Microsoft_Intune_Enrollment/AutopilotDevices.ReactView/filterOnManualRemediationRequired~/false";
    public string UpdateStockroomsUrl { get; set; } = "https://reedelsevier.service-now.com/now/nav/ui/classic/params/target/alm_hardware_list.do%3Fsysparm_first_row%3D1%26sysparm_query%3Dserial_number%3D{SERIAL}%26sysparm_query_encoded%3Dserial_number%3D{SERIAL}%26sysparm_view%3D";
}

public static class QaComputerNaming
{
    private static bool Useful(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^(?:unknown|unavailable|not set|n/?a|none)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool GenericWindowsName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^(?:DESKTOP|WIN|MININT)-[A-Z0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string Resolve(AppConfig config, CachedWindowsSnapshot hardware, WindowsQaSessionCache? cache = null)
    {
        var serial = new[] { cache?.ServiceTag, hardware.SerialNumber }.FirstOrDefault(Useful)?.Trim() ?? "";
        var computer = Useful(hardware.ComputerName) && !GenericWindowsName(hardware.ComputerName)
            ? hardware.ComputerName.Trim()
            : serial;
        var asset = new[] { hardware.AssetTag, cache?.AssetTag }.FirstOrDefault(Useful)?.Trim() ?? "";
        var format = string.IsNullOrWhiteSpace(config.QaComputerNameFormat) ? "LNG-{serial}" : config.QaComputerNameFormat.Trim();
        var resolved = format
            .Replace("{serial}", serial, StringComparison.OrdinalIgnoreCase)
            .Replace("{computer}", computer, StringComparison.OrdinalIgnoreCase)
            .Replace("{asset}", asset, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return Useful(resolved) ? resolved : new[] { computer, serial, asset }.FirstOrDefault(Useful)?.Trim() ?? "Laptop";
    }
}

public sealed record DiagnosticsResult(string State, string MainText, string DetailText, string Path, string RawText, bool UnansweredPrompt);

public sealed class CachedWindowsSnapshot
{
    public string ComputerName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string AssetTag { get; set; } = "";
    public string Warranty { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Memory { get; set; } = "";
    public string Gpu { get; set; } = "";
    public string Storage { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Bios { get; set; } = "";
    public string Battery { get; set; } = "Battery information unavailable in Windows cache";

    public string Summary => $"Source: Cached Windows QA session\nDevice Name: {(string.IsNullOrWhiteSpace(DeviceName) ? ComputerName : DeviceName)}\nManufacturer: {Manufacturer}\nModel: {Model}\nSerial Number: {SerialNumber}\nAsset Tag: {AssetTag}\nWarranty: {Warranty}\nCPU: {Cpu}\nMemory: {Memory}\nGPU: {Gpu}\nStorage: {Storage}\nOperating System: {OperatingSystem}\nBIOS: {Bios}\n{Battery}";
}

public sealed class WindowsQaSessionCache
{
    public DateTime SavedAt { get; set; }
    public string ServiceTag { get; set; } = "";
    public string AssetTag { get; set; } = "";
    public string Warranty { get; set; } = "";
    public string BatterySummary { get; set; } = "";
    public string BatteryHealthRating { get; set; } = "";
    public CachedHardware? Hardware { get; set; }
    public string SecureBootState { get; set; } = "";
    public string BiosStatusText { get; set; } = "";
    public Dictionary<string, CachedQaStep> Steps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool? FinalHashGroupTag { get; set; }
    public bool? FinalCleanedLaptop { get; set; }
    public bool? FinalUpdateStockrooms { get; set; }
    public bool? FinalTrackpadWorking { get; set; }
    public bool? FinalDeletedUser { get; set; }
    public bool? FinalConditionSuitableForUse { get; set; }
    public bool UsbPortTestFinished { get; set; }
    public List<UsbPortCache> UsbPorts { get; set; } = new();
    public string RmaIssues { get; set; } = "";
    public string RepairNotes { get; set; } = "";
    public string DiagnosticsLogPath { get; set; } = "";
    public string DiagnosticsRawText { get; set; } = "";
}

public sealed class UsbPortCache
{
    public string Label { get; set; } = "";
    public bool Passed { get; set; }
    public bool Failed { get; set; }
    public string LocationPath { get; set; } = "";
    public string DeviceName { get; set; } = "";
}

public sealed class CachedQaStep
{
    public string State { get; set; } = "Waiting";
    public string MainText { get; set; } = "";
    public string DetailText { get; set; } = "";
}

public sealed class CachedHardware
{
    public string Computer { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string PhysicalMemory { get; set; } = "";
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string OsBuild { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Memory { get; set; } = "";
    public string Gpu { get; set; } = "";
    public string Storage { get; set; } = "";
    public string Bios { get; set; } = "";
}
