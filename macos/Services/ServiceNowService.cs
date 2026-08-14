using System.Text.RegularExpressions;

namespace LaptopQA.Mac.Services;

public static class ServiceNowService
{
    public static string BuildDescription(CachedWindowsSnapshot hardware)
    {
        var model = ModelNumber(hardware.Model);
        var serial = string.IsNullOrWhiteSpace(hardware.SerialNumber) ? "serial unavailable" : hardware.SerialNumber.Trim();
        var asset = string.IsNullOrWhiteSpace(hardware.AssetTag) ? "asset unavailable" : hardware.AssetTag.Trim();
        return $"Laptop QA | {model} | {serial} | {asset}";
    }

    private static string ModelNumber(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "model unavailable";
        var parts = Regex.Matches(model.Trim(), @"[A-Za-z]*\d+[A-Za-z0-9-]*")
            .Select(match => match.Value)
            .ToArray();
        return parts.Length == 0 ? model.Trim() : parts[^1];
    }
}
