using SkiaSharp;
using LaptopQA.Shared;

namespace LaptopQATestingMac.Services;

public sealed record QaSheetData(
    AppConfig Config,
    CachedWindowsSnapshot Hardware,
    WindowsQaSessionCache Cache,
    DiagnosticsResult Diagnostics,
    bool HashGroupTag,
    bool Cleaned,
    bool Stockrooms,
    bool Trackpad,
    bool RemovedUser,
    bool ConditionSuitable,
    string RmaIssues,
    string RepairNotes);

public static class QaSheetService
{
    private const int Width = 1600;
    private const int Height = 2500;

    public static string Create(string folder, QaSheetData data)
    {
        Directory.CreateDirectory(folder);
        var identifier = Identifier(data);
        var file = Path.Combine(folder, $"{Safe(identifier)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-QA-Sheet.png");
        Render(file, data);
        return file;
    }

    private static bool IsUsefulIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^(?:unknown|unavailable|not set|n/?a|none)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string Identifier(QaSheetData data) => QaComputerNaming.Resolve(data.Config, data.Hardware, data.Cache);

    private static void Render(string path, QaSheetData d)
    {
        string T(string text) => UiLocalization.Text(d.Config.AppLanguage, text);
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var regular = Typeface("Arial");
        using var bold = Typeface("Arial", SKFontStyle.Bold);
        using var title = Paint(52, SKColors.White, bold);
        using var heading = Paint(27, Color("#18333D"), bold);
        using var label = Paint(18, Color("#60757E"), bold);
        using var value = Paint(23, Color("#13252D"), bold);
        using var body = Paint(21, Color("#263B44"), regular);
        using var bodyBold = Paint(22, Color("#13252D"), bold);
        using var small = Paint(17, Color("#60757E"), regular);

        using (var headerPaint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Width, 180), [Color("#18333D"), Color("#5F858D")], null, SKShaderTileMode.Clamp) })
            canvas.DrawRect(0, 0, Width, 180, headerPaint);
        canvas.DrawText(T("Laptop QA Testing"), 62, 83, title);
        using (var subtitle = Paint(23, Color("#D8E8EC"), regular))
            canvas.DrawText(T("Quality assurance summary"), 65, 125, subtitle);

        var rows = BuildRows(d);
        var overall = rows.Any(r => r.State == "Bad") ? "Needs Attention" : rows.Any(r => r.State == "Warning") ? "Warning" : rows.All(r => r.State is "Ok" or "Ignored") ? "Passed" : "Incomplete";
        FillRound(canvas, new SKRect(1250, 38, 1538, 142), Color("#3D6974"), 18);
        using (var overallLabel = Paint(17, Color("#D8E8EC"), bold)) canvas.DrawText(T("OVERALL"), 1328, 77, overallLabel);
        using (var overallValue = Paint(27, SKColors.White, bold)) DrawCentered(canvas, T(overall), new SKRect(1260, 80, 1528, 132), overallValue);

        var meta = new[]
        {
            (T("Device Name"), Identifier(d)), (T("Technician"), d.Config.TechnicianName), (T("Date"), DateTime.Now.ToString("g")),
            (T("Manufacturer"), d.Hardware.Manufacturer), (T("Model"), d.Hardware.Model), (T("Service Tag"), d.Hardware.SerialNumber),
            (T("Asset Number"), d.Hardware.AssetTag), (T("Warranty"), d.Hardware.Warranty)
        };
        var cellWidth = 360f;
        for (var i = 0; i < meta.Length; i++)
        {
            var x = 55 + ((i % 4) * 382);
            var y = 220 + ((i / 4) * 112);
            Field(canvas, new SKRect(x, y, x + cellWidth, y + 92), meta[i].Item1, meta[i].Item2, label, value);
        }

        canvas.DrawText(T("Hardware Specs").ToUpperInvariant(), 58, 486, heading);
        var hardware = new[] { ("CPU", d.Hardware.Cpu), (T("Memory").ToUpperInvariant(), d.Hardware.Memory), ("GPU", d.Hardware.Gpu), (T("Storage").ToUpperInvariant(), d.Hardware.Storage) };
        FillRound(canvas, new SKRect(55, 510, 1545, 690), Color("#FBFCFD"), 15, Color("#CBD9DF"));
        for (var i = 0; i < hardware.Length; i++)
        {
            var x = 82 + ((i % 2) * 744);
            var y = 548 + ((i / 2) * 72);
            canvas.DrawText(hardware[i].Item1, x, y, label);
            DrawClipped(canvas, hardware[i].Item2, x + 138, y, 560, value);
        }

        canvas.DrawText(T("QA Results").ToUpperInvariant(), 58, 752, heading);
        var tableLeft = 55f;
        var tableTop = 778f;
        FillRound(canvas, new SKRect(tableLeft, tableTop, 1545, tableTop + 60), Color("#244F5C"), 10);
        using (var th = Paint(18, SKColors.White, bold))
        {
            canvas.DrawText(T("TASK"), 78, tableTop + 39, th);
            canvas.DrawText(T("STATUS"), 654, tableTop + 39, th);
            canvas.DrawText(T("DETAIL"), 858, tableTop + 39, th);
        }

        var rowTop = tableTop + 60;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var top = rowTop + (i * 78);
            using var fill = new SKPaint { Color = i % 2 == 0 ? SKColors.White : Color("#F6F9FA") };
            canvas.DrawRect(tableLeft, top, 1490, 78, fill);
            using var line = new SKPaint { Color = Color("#D7E1E5"), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            canvas.DrawRect(tableLeft, top, 1490, 78, line);
            DrawWrapped(canvas, row.Task, 78, top + 30, 539, bodyBold, 2, 24);
            StatusPill(canvas, row.State, new SKRect(650, top + 19, 820, top + 59), bold, d.Config.AppLanguage);
            DrawWrapped(canvas, row.Detail, 858, top + 27, 650, body, 2, 23);
        }

        var notesTop = rowTop + (rows.Count * 78) + 54;
        canvas.DrawText(T("Notes").ToUpperInvariant(), 58, notesTop, heading);
        var rmaTop = notesTop + 24;
        NoteBox(canvas, new SKRect(55, rmaTop, 1545, rmaTop + 148), T("RMA Issues").ToUpperInvariant(), d.RmaIssues, label, body);
        var repairTop = rmaTop + 164;
        NoteBox(canvas, new SKRect(55, repairTop, 1545, repairTop + 200), T("Repair Notes").ToUpperInvariant(), d.RepairNotes, label, body);
        var footerTop = repairTop + 228;
        using var footerLine = new SKPaint { Color = Color("#D7E1E5"), StrokeWidth = 2 };
        canvas.DrawLine(55, footerTop, 1545, footerTop, footerLine);
        canvas.DrawText($"{T("Generated")}: {DateTime.Now:G}", 58, footerTop + 31, small);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(path);
        png.SaveTo(stream);
    }

    private static List<RowData> BuildRows(QaSheetData d)
    {
        string T(string text) => UiLocalization.Text(d.Config.AppLanguage, text);
        CachedQaStep Step(string name, string fallback) => d.Cache.Steps.TryGetValue(name, out var step) ? step : new CachedQaStep { State = "Waiting", DetailText = fallback };
        string Detail(CachedQaStep step, string fallback) => string.IsNullOrWhiteSpace(step.DetailText) ? fallback : step.DetailText;
        var wifi = Step("WiFi", "Wi-Fi not checked yet."); var ethernet = Step("Ethernet", "Ethernet not checked yet.");
        var camera = Step("Camera", "Camera not checked yet."); var external = Step("ExternalVideo", "External video not checked yet."); var keyboard = Step("Keyboard", "Keyboard not checked yet.");
        var usbState = !d.Cache.UsbPortTestFinished
            ? "Waiting"
            : d.Cache.UsbPorts.Any(port => port.Failed) ? "Bad" : "Ok";
        var usbDetail = d.Cache.UsbPorts.Count == 0
            ? "USB port count unavailable from BIOS connector data."
            : $"{d.Cache.UsbPorts.Count(port => port.Passed)} passed, {d.Cache.UsbPorts.Count(port => port.Failed)} failed, {d.Cache.UsbPorts.Count(port => !port.Passed && !port.Failed)} pending.";
        return
        [
            new("2", T("Wi-Fi connected or SSIDs visible"), wifi.State, T(Detail(wifi, "Wi-Fi not checked yet."))),
            new("2", T("Ethernet adapter is Up"), ethernet.State, T(Detail(ethernet, "Ethernet not checked yet."))),
            new("3", T("Camera, audio restore, and Camera Roll cleanup"), camera.State, T(Detail(camera, "Camera not checked yet."))),
            new("4", T("External display video verified"), external.State, T(Detail(external, "External video not checked yet."))),
            new("5", T("Keyboard test result"), keyboard.State, T(Detail(keyboard, "Keyboard not checked yet."))),
            new("6", T("Dell preboot diagnostics"), d.Diagnostics.State, T(string.IsNullOrWhiteSpace(d.Diagnostics.DetailText) ? "Diagnostics log not found." : d.Diagnostics.DetailText)),
            new("7", T("USB ports verified"), usbState, usbDetail),
            new("", T("Battery health checked"), BatteryState(d.Hardware.Battery), T(string.IsNullOrWhiteSpace(d.Hardware.Battery) ? "Battery information unavailable in Windows cache" : d.Hardware.Battery)),
            Check("8", "Hash and group tag checked", d.HashGroupTag, d.Config.AppLanguage), Check("8", "Laptop cleaned", d.Cleaned, d.Config.AppLanguage),
            Check("8", "Removed User from Laptop in Intune", d.RemovedUser, d.Config.AppLanguage), Check("8", "Update Stockrooms", d.Stockrooms, d.Config.AppLanguage), Check("8", "Trackpad working", d.Trackpad, d.Config.AppLanguage),
            Check("8", "Physical condition suitable for use", d.ConditionSuitable, d.Config.AppLanguage)
        ];
    }

    private static string BatteryState(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ? "Waiting" : "Ok";

    private static RowData Check(string number, string task, bool value, string languageCode) =>
        new(number, UiLocalization.Text(languageCode, task), value ? "Ok" : "Waiting",
            UiLocalization.Text(languageCode, value ? $"{task} checked off." : "Not checked off."));
    private static SKTypeface Typeface(string family, SKFontStyle? style = null) => SKTypeface.FromFamilyName(family, style ?? SKFontStyle.Normal) ?? SKTypeface.Default;
    private static SKPaint Paint(float size, SKColor color, SKTypeface face) => new() { IsAntialias = true, TextSize = size, Color = color, Typeface = face };
    private static SKColor Color(string hex) => SKColor.Parse(hex);

    private static void FillRound(SKCanvas canvas, SKRect rect, SKColor fill, float radius, SKColor? stroke = null)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = fill };
        canvas.DrawRoundRect(rect, radius, radius, paint);
        if (stroke is null) return;
        using var border = new SKPaint { IsAntialias = true, Color = stroke.Value, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawRoundRect(rect, radius, radius, border);
    }

    private static void Field(SKCanvas canvas, SKRect rect, string name, string? text, SKPaint label, SKPaint value)
    {
        FillRound(canvas, rect, Color("#F7FAFB"), 13, Color("#CBD9DF"));
        canvas.DrawText(name.ToUpperInvariant(), rect.Left + 17, rect.Top + 29, label);
        DrawClipped(canvas, text, rect.Left + 17, rect.Top + 64, rect.Width - 34, value);
    }

    private static void NoteBox(SKCanvas canvas, SKRect rect, string name, string? text, SKPaint label, SKPaint body)
    {
        FillRound(canvas, rect, Color("#FBFCFD"), 13, Color("#CBD9DF"));
        canvas.DrawText(name, rect.Left + 17, rect.Top + 30, label);
        var maxLines = Math.Max(1, (int)Math.Floor((rect.Height - 65) / 25) + 1);
        DrawWrapped(canvas, text?.Trim() ?? "", rect.Left + 17, rect.Top + 65, rect.Width - 34, body, maxLines, 25, "");
    }

    private static void StatusPill(SKCanvas canvas, string state, SKRect rect, SKTypeface bold, string languageCode)
    {
        var (text, foreground, background, border) = state switch
        {
            "Ok" => ("PASS", "#0F5132", "#D9F5E6", "#A9E6C1"),
            "Bad" => ("FAIL", "#842029", "#FDE2E4", "#F3B4BB"),
            "Ignored" => ("IGNORED", "#465A62", "#EEF3F5", "#CCD8DE"),
            "Warning" => ("CAUTION", "#6B4D00", "#FFF2C2", "#F2D36B"),
            "Working" => ("IN PROGRESS", "#6B4D00", "#FFF2C2", "#F2D36B"),
            _ => ("NOT RUN", "#465A62", "#EEF3F5", "#CCD8DE")
        };
        FillRound(canvas, rect, Color(background), 22, Color(border));
        using var paint = Paint(17, Color(foreground), bold);
        DrawCentered(canvas, UiLocalization.Text(languageCode, text), rect, paint);
    }

    private static void DrawCentered(SKCanvas canvas, string text, SKRect rect, SKPaint paint)
    {
        var width = paint.MeasureText(text);
        canvas.DrawText(text, rect.MidX - (width / 2), rect.MidY - ((paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2), paint);
    }

    private static void DrawClipped(SKCanvas canvas, string? text, float x, float baseline, float maxWidth, SKPaint paint)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "Not available" : text.Trim();
        var original = value;
        while (value.Length > 1 && paint.MeasureText(value) > maxWidth) value = value[..^1];
        if (!string.Equals(value, original, StringComparison.Ordinal) && value.Length > 2) value = value[..^1] + "…";
        canvas.DrawText(value, x, baseline, paint);
    }

    private static void DrawWrapped(SKCanvas canvas, string? text, float x, float firstBaseline, float maxWidth, SKPaint paint, int maxLines, float lineHeight, string emptyFallback = "Not available")
    {
        var words = (string.IsNullOrWhiteSpace(text) ? emptyFallback : text.Replace('\r', ' ').Replace('\n', ' ')).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (paint.MeasureText(candidate) <= maxWidth) { current = candidate; continue; }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
            current = word;
            if (lines.Count == maxLines) break;
        }
        if (lines.Count < maxLines && !string.IsNullOrEmpty(current)) lines.Add(current);
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        var consumed = string.Join(" ", lines);
        if (consumed.Length < string.Join(" ", words).Length && lines.Count > 0)
        {
            var last = lines[^1];
            while (last.Length > 1 && paint.MeasureText(last + "…") > maxWidth) last = last[..^1];
            lines[^1] = last + "…";
        }
        for (var i = 0; i < lines.Count; i++) canvas.DrawText(lines[i], x, firstBaseline + (i * lineHeight), paint);
    }

    private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : string.Concat(value.Where(char.IsLetterOrDigit));
    private sealed record RowData(string Number, string Task, string State, string Detail);
}
