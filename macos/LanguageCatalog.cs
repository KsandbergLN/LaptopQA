using System.Globalization;

namespace LaptopQATestingMac;

public sealed record AppLanguage(string Code, string Name)
{
    public override string ToString() => Name;
}

public static class LanguageCatalog
{
    private static IReadOnlyList<AppLanguage> LegacyAll { get; } =
    [
        new("en-US", "English"), new("es-ES", "Español"), new("fr-FR", "Français"),
        new("de-DE", "Deutsch"), new("pt-BR", "Português"), new("it-IT", "Italiano"),
        new("ru-RU", "Русский"), new("ar-SA", "العربية"), new("hi-IN", "हिन्दी"),
        new("zh-CN", "简体中文"), new("ja-JP", "日本語"), new("ko-KR", "한국어")
    ];

    public static IReadOnlyList<AppLanguage> All { get; } =
    [
        new("en-US", "English"), new("es-ES", "Espa\u00F1ol"), new("fr-FR", "Fran\u00E7ais"),
        new("de-DE", "Deutsch"), new("pt-BR", "Portugu\u00EAs"), new("zh-CN", "\u7B80\u4F53\u4E2D\u6587"),
        new("ja-JP", "\u65E5\u672C\u8A9E"), new("hi-IN", "\u0939\u093F\u0928\u094D\u0926\u0940"),
        new("bn-IN", "\u09AC\u09BE\u0982\u09B2\u09BE"), new("ta-IN", "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD"),
        new("te-IN", "\u0C24\u0C46\u0C32\u0C41\u0C17\u0C41"), new("mr-IN", "\u092E\u0930\u093E\u0920\u0940"),
        new("ar-SA", "\u0627\u0644\u0639\u0631\u0628\u064A\u0629")
    ];

    public static AppLanguage Resolve(string? code) =>
        All.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static void ApplyCulture(string? code)
    {
        var culture = CultureInfo.GetCultureInfo(Resolve(code).Code);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
