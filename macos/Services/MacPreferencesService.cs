using System.Diagnostics;

namespace LaptopQATestingMac.Services;

public static class MacPreferencesService
{
    private const string Domain = "com.reedelsevier.laptopqa";
    private const string ServiceNowInstructionsKey = "ServiceNowInstructionsShownV2";

    public static bool ServiceNowInstructionsShown()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        var result = RunDefaults("read", Domain, ServiceNowInstructionsKey);
        return result.ExitCode == 0 &&
               (result.Output.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public static void MarkServiceNowInstructionsShown()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var result = RunDefaults("write", Domain, ServiceNowInstructionsKey, "-bool", "true");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("The one-time ServiceNow instructions preference could not be saved."
                + (string.IsNullOrWhiteSpace(result.Error) ? "" : $" {result.Error.Trim()}"));
    }

    public static void ResetServiceNowInstructions()
    {
        if (!OperatingSystem.IsMacOS()) return;
        _ = RunDefaults("delete", Domain, ServiceNowInstructionsKey);
    }

    private static (int ExitCode, string Output, string Error) RunDefaults(params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "/usr/bin/defaults",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The macOS preferences service could not start.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
