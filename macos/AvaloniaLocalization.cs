using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using LaptopQA.Shared;

namespace LaptopQA.Mac;

public static class AvaloniaLocalization
{
    public static void Apply(Control root, string? languageCode)
    {
        var flowDirection = string.Equals(languageCode, "ar-SA", StringComparison.OrdinalIgnoreCase)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            if (control is Window window) window.Title = UiLocalization.Text(languageCode, window.Title);
            if (control is TextBlock textBlock) { textBlock.Text = UiLocalization.Text(languageCode, textBlock.Text); textBlock.FlowDirection = flowDirection; }
            if (control is TextBox textBox) { textBox.Text = UiLocalization.Text(languageCode, textBox.Text); textBox.FlowDirection = flowDirection; }
            if (control is ContentControl contentControl && contentControl.Content is string content)
            {
                var translated = UiLocalization.Text(languageCode, content);
                contentControl.Content = translated;
                contentControl.FlowDirection = flowDirection;
                if (contentControl is Button button && translated.Length > 12 && button.Width is > 0 and < 160)
                    button.FontSize = Math.Min(button.FontSize, 10.5);
                if (contentControl is CheckBox checkBox && translated.Length > 34)
                    checkBox.FontSize = Math.Min(checkBox.FontSize, 10.5);
            }
            if (ToolTip.GetTip(control) is string tip)
                ToolTip.SetTip(control, UiLocalization.Text(languageCode, tip));
        }
    }
}
