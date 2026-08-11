using System.IO;
using System.Text.RegularExpressions;

namespace LaptopQATestingV4;

internal static class ErrorLog
{
    private static readonly object Gate = new();
    private static readonly string Root = ResolveDataRoot();
    public static readonly string LogsDir = Path.Combine(Root, "logs");
    public static readonly string ActivityDir = Path.Combine(Root, "activity");
    private static readonly string LegacyActivityLogsDir = Path.Combine(LogsDir, "activity");
    private static readonly string LegacyErrorLogsDir = Path.Combine(LogsDir, "errors");
    private static readonly string SessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
    private static string _activityLogPath = "";
    private static string _errorLogPath = "";

    public static string ActivityLogPath
    {
        get
        {
            Initialize();
            lock (Gate) return _activityLogPath;
        }
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ActivityDir);
        MigrateLegacyLogs(LegacyActivityLogsDir, ActivityDir);
        MigrateLegacyLogs(LegacyErrorLogsDir, LogsDir);
        MigrateRootActivityLogs();
        if (string.IsNullOrWhiteSpace(_activityLogPath)) StartSession("Laptop");
    }

    public static void StartSession(string? computerName)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(ActivityDir);
            var computer = SafeFileName(computerName, "Computer");
            var activityPath = Path.Combine(ActivityDir, $"{computer}-{SessionStamp}-Activity.log");
            var errorPath = Path.Combine(LogsDir, $"{computer}-{SessionStamp}-Errors.log");

            MoveCurrentSessionLog(ref _activityLogPath, activityPath);
            MoveCurrentSessionLog(ref _errorLogPath, errorPath);
        }
    }

    private static void MoveCurrentSessionLog(ref string currentPath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            currentPath = destinationPath;
            return;
        }

        if (string.Equals(currentPath, destinationPath, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            if (File.Exists(currentPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                if (File.Exists(destinationPath))
                {
                    File.AppendAllText(destinationPath, File.ReadAllText(currentPath));
                    File.Delete(currentPath);
                }
                else
                {
                    File.Move(currentPath, destinationPath);
                }
            }
        }
        catch
        {
            // A filename refresh must never interrupt the QA workflow.
            return;
        }

        currentPath = destinationPath;
    }

    public static bool ShouldLogActivity(string message) =>
        Regex.IsMatch(message, @"\b(failed|failure|error|exception|timed out|access denied|denied|not accepted|not supported|not found|not loaded|not verified|unavailable|could not)\b", RegexOptions.IgnoreCase);

    public static void WriteActivity(string section, string message)
    {
        try
        {
            Initialize();
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{section}] {Redact(message)}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(_activityLogPath, text);
            }
        }
        catch
        {
            // Logging must never break the QA workflow.
        }
    }

    public static void WriteError(string section, string message)
    {
        Write("Activity Error", section, message, null);
    }

    public static void WriteException(string section, string message, Exception exception)
    {
        Write("Exception", section, message, exception);
    }

    private static void Write(string kind, string section, string message, Exception? exception)
    {
        try
        {
            Initialize();
            var text = $"""
[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{kind}] [{section}]
Message:
{Redact(message)}
""";

            if (exception is not null)
            {
                text += $"""

Exception:
{Redact(exception.ToString())}
""";
            }

            text += $"{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(_errorLogPath, text);
            }
        }
        catch
        {
            // Logging must never break the QA workflow.
        }
    }

    private static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var redacted = text;
        redacted = Regex.Replace(redacted, @"(?i)(client_secret|shared secret|DellWarrantyClientSecret|DellWarrantyClientSecretProtected)(\s*[:=]\s*)([^\s,;""']+)", "$1$2[redacted]");
        redacted = Regex.Replace(redacted, @"(?i)(Authorization:\s*Bearer\s+)([A-Za-z0-9._~+/=-]+)", "$1[redacted]");
        redacted = Regex.Replace(redacted, @"(?i)(access_token[""']?\s*[:=]\s*[""']?)([A-Za-z0-9._~+/=-]+)", "$1[redacted]");
        return redacted;
    }

    private static void MigrateLegacyLogs(string legacyFolder, string destinationFolder)
    {
        try
        {
            if (!Directory.Exists(legacyFolder)) return;
            foreach (var source in Directory.EnumerateFiles(legacyFolder))
            {
                var destination = Path.Combine(destinationFolder, Path.GetFileName(source));
                if (File.Exists(destination))
                {
                    destination = Path.Combine(destinationFolder,
                        $"{Path.GetFileNameWithoutExtension(source)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{Path.GetExtension(source)}");
                }
                File.Move(source, destination);
            }
            if (!Directory.EnumerateFileSystemEntries(legacyFolder).Any()) Directory.Delete(legacyFolder);
        }
        catch
        {
            // A legacy migration failure must not interrupt QA startup.
        }
    }

    private static void MigrateRootActivityLogs()
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return;
            Directory.CreateDirectory(ActivityDir);
            foreach (var source in Directory.EnumerateFiles(LogsDir, "*Activity*", SearchOption.TopDirectoryOnly))
            {
                var destination = Path.Combine(ActivityDir, Path.GetFileName(source));
                if (File.Exists(destination))
                {
                    destination = Path.Combine(ActivityDir,
                        $"{Path.GetFileNameWithoutExtension(source)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}{Path.GetExtension(source)}");
                }
                File.Move(source, destination);
            }
        }
        catch
        {
            // A legacy migration failure must not interrupt QA startup.
        }
    }

    private static string SafeFileName(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(source.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string ResolveDataRoot()
    {
        var configured = GetDataRootFromArgs() ?? Environment.GetEnvironmentVariable("LAPTOP_QA_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) return AppContext.BaseDirectory;

        try
        {
            return Path.GetFullPath(configured);
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static string? GetDataRootFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--data-root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--data-root=".Length..];
            }
        }

        return null;
    }
}
