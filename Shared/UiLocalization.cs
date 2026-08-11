using System.Reflection;
using System.Text.Json;

namespace LaptopQA.Shared;

public static class UiLocalization
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Packs = new(Load);

    public static string Text(string? languageCode, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var canonical = Canonical(value);
        var code = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode;
        if (!Packs.Value.TryGetValue(code, out var pack)) return canonical;
        if (pack.TryGetValue(canonical, out var translated)) return translated;

        var separator = canonical.IndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0) return canonical;
        var prefix = Canonical(canonical[..separator]);
        if (!pack.TryGetValue(prefix, out var translatedPrefix)) return canonical;
        var suffix = canonical[(separator + 2)..];
        var percentAt = suffix.LastIndexOf(" (", StringComparison.Ordinal);
        var suffixValue = percentAt > 0 ? suffix[..percentAt] : suffix;
        var suffixTail = percentAt > 0 ? suffix[percentAt..] : "";
        var canonicalSuffix = Canonical(suffixValue);
        var translatedSuffix = pack.TryGetValue(canonicalSuffix, out var localizedSuffix) ? localizedSuffix : suffixValue;
        return $"{translatedPrefix}: {translatedSuffix}{suffixTail}";
    }

    public static bool HasLanguage(string languageCode) =>
        Packs.Value.TryGetValue(languageCode, out var pack) && pack.Count > 0;

    private static string Canonical(string value)
    {
        if (Packs.Value.TryGetValue("en-US", out var english) && english.ContainsKey(value)) return value;
        foreach (var pack in Packs.Value.Values)
        {
            foreach (var item in pack)
            {
                if (string.Equals(item.Value, value, StringComparison.Ordinal)) return item.Key;
            }
        }
        return value;
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("ui-translations.json", StringComparison.OrdinalIgnoreCase) ||
                                    name.EndsWith("UITranslations.json", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return new(StringComparer.OrdinalIgnoreCase);
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null) return new(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
               ?? new(StringComparer.OrdinalIgnoreCase);
    }
}
