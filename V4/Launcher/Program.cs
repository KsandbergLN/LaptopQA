using System.Diagnostics;
using System.Runtime.InteropServices;

var root = AppContext.BaseDirectory;
var scriptPath = Path.Combine(root, "Windows Laptop QA Launcher.vbs");
if (!File.Exists(scriptPath))
{
    scriptPath = Path.Combine(root, "LAPTOP QA", "Windows Laptop QA Launcher.vbs");
}

if (!File.Exists(scriptPath))
{
    var parent = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
    if (!string.IsNullOrWhiteSpace(parent))
    {
        var parentScript = Path.Combine(parent, "Windows Laptop QA Launcher.vbs");
        if (File.Exists(parentScript))
        {
            scriptPath = parentScript;
        }
    }
}

if (!File.Exists(scriptPath))
{
    MessageBox(IntPtr.Zero,
        $"Laptop QA could not find the startup script:\n\n{scriptPath}\n\nKeep this launcher next to Windows Laptop QA Launcher.vbs.",
        "Laptop QA",
        0x00000010);
    return 1;
}

try
{
    var wscriptPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wscript.exe");
    if (!File.Exists(wscriptPath))
    {
        wscriptPath = "wscript.exe";
    }

    using var process = Process.Start(new ProcessStartInfo(wscriptPath)
    {
        WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? root,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        Arguments = $"\"{scriptPath}\""
    });

    return process is null ? 2 : 0;
}
catch (Exception ex)
{
    MessageBox(IntPtr.Zero,
        $"Laptop QA could not start the VBS startup script:\n\n{scriptPath}\n\n{ex.Message}",
        "Laptop QA",
        0x00000010);
    return 2;
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
