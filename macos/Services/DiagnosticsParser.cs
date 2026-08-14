using System.Text.RegularExpressions;

namespace LaptopQA.Mac.Services;

public static class DiagnosticsParser
{
    public static DiagnosticsResult Parse(string path, string raw)
    {
        var failures = new List<string>();
        var currentTest = "";
        var currentTestHasUnansweredPrompt = false;
        var hasUnansweredPrompt = false;
        var unansweredPrompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            var testMatch = Regex.Match(line, @"^\*\*\s*(.+?)\s*\*\*$");
            if (testMatch.Success)
            {
                currentTest = Humanize(testMatch.Groups[1].Value);
                currentTestHasUnansweredPrompt = false;
                continue;
            }

            if (IsUnansweredPrompt(line))
            {
                var test = Condense(currentTest);
                if (failures.Any(x => string.Equals(x, test, StringComparison.OrdinalIgnoreCase)))
                {
                    hasUnansweredPrompt = true;
                    currentTestHasUnansweredPrompt = true;
                    unansweredPrompts.Add(PromptLabel(currentTest, line));
                    RemoveCurrentFailure(failures, currentTest);
                }
            }

            if (Regex.IsMatch(line, @"^Test Result:\s*Fail\b", RegexOptions.IgnoreCase) && !string.IsNullOrWhiteSpace(currentTest))
            {
                var test = Condense(currentTest);
                failures.RemoveAll(x => string.Equals(x, test, StringComparison.OrdinalIgnoreCase));
                if (!currentTestHasUnansweredPrompt) failures.Add(test);
                continue;
            }

            if (Regex.IsMatch(line, @"^Test Result:\s*Success\b", RegexOptions.IgnoreCase) && !string.IsNullOrWhiteSpace(currentTest))
            {
                RemoveCurrentFailure(failures, currentTest);
                currentTestHasUnansweredPrompt = false;
            }
        }

        failures = failures.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (failures.Count > 0)
        {
            var detail = string.Join("; ", failures.Take(3));
            if (failures.Count > 3) detail += $"; plus {failures.Count - 3} more";
            if (hasUnansweredPrompt) detail += "; " + DescribeUnansweredPrompts(unansweredPrompts);
            return new("Bad", "Diagnostics failed", detail, path, raw, hasUnansweredPrompt);
        }

        if (hasUnansweredPrompt)
            return new("Warning", "Diagnostics completed with warning", DescribeUnansweredPrompts(unansweredPrompts), path, raw, true);

        return Regex.IsMatch(raw, @"Test Result:\s*Success\b", RegexOptions.IgnoreCase)
            ? new("Ok", "Passed all diagnostics tests", "Dell preboot diagnostics reported no failed tests.", path, raw, false)
            : new("Warning", "Diagnostics results unavailable", "No completed diagnostics results were detected in the log.", path, raw, false);
    }

    public static bool IsUnansweredPrompt(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value,
        @"(?:\b(?:no|without)\s+(?:user\s+|technician\s+)?(?:response|answer|feedback)\b|\b(?:user|technician)\s+(?:did\s+not|didn't|failed\s+to)\s+(?:respond|answer|provide\s+(?:a\s+)?(?:response|answer|feedback))\b|\b(?:response|answer|feedback)\s+(?:was\s+)?(?:not\s+(?:received|provided|entered)|missing|unavailable)\b|\b(?:prompt|user interaction).*\b(?:timed?\s*out|timeout|unanswered)\b|\b(?:user|technician).*\bno\s+(?:input|selection)\b)", RegexOptions.IgnoreCase);

    private static void RemoveCurrentFailure(List<string> failures, string currentTest)
    {
        if (string.IsNullOrWhiteSpace(currentTest)) return;
        var test = Condense(currentTest);
        failures.RemoveAll(x => string.Equals(x, test, StringComparison.OrdinalIgnoreCase));
    }

    private static string Humanize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    private static string PromptLabel(string currentTest, string promptLine)
    {
        var test = Condense(currentTest);
        if (!string.IsNullOrWhiteSpace(test)) return test;
        if (Regex.IsMatch(promptLine, @"\b(?:video|graphics?|display|lcd|screen)\b", RegexOptions.IgnoreCase)) return "Video";
        if (Regex.IsMatch(promptLine, @"\b(?:audio|speaker|tone|sound)\b", RegexOptions.IgnoreCase)) return "Audio";
        if (Regex.IsMatch(promptLine, @"\b(?:camera|webcam)\b", RegexOptions.IgnoreCase)) return "Camera";
        if (Regex.IsMatch(promptLine, @"\b(?:keyboard|key)\b", RegexOptions.IgnoreCase)) return "Keyboard";
        if (Regex.IsMatch(promptLine, @"\b(?:touchpad|trackpad|mouse|pointer)\b", RegexOptions.IgnoreCase)) return "Pointing device";
        return "diagnostics";
    }

    private static string DescribeUnansweredPrompts(IEnumerable<string> prompts)
    {
        var labels = prompts.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (labels.Count == 0) return "Technician did not respond to a diagnostics prompt.";
        return labels.Count == 1
            ? $"Technician did not respond to the {labels[0]} prompt."
            : "Technician did not respond to the following prompts: " + string.Join(", ", labels) + ".";
    }

    private static string Condense(string value)
    {
        var text = Humanize(value);
        text = Regex.Replace(text, @"\s*\((?:Error|Validate code).+?\)\s*$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+Error:\s*[0-9:]+.*$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+Validate code:\s*\d+.*$", "", RegexOptions.IgnoreCase);
        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) text = text[..dash].Trim();
        return text.Length <= 80 ? text : text[..77].TrimEnd() + "...";
    }
}
