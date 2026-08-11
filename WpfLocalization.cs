using System.Collections;
using System.Windows;
using System.Windows.Controls;
using LaptopQA.Shared;

namespace LaptopQA.Windows;

public static class WpfLocalization
{
    public static void Apply(DependencyObject root, string? languageCode)
    {
        var flowDirection = string.Equals(languageCode, "ar-SA", StringComparison.OrdinalIgnoreCase)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        if (root is Window window) window.Title = UiLocalization.Text(languageCode, window.Title);
        if (root is TextBlock textBlock) { textBlock.Text = UiLocalization.Text(languageCode, textBlock.Text); textBlock.FlowDirection = flowDirection; }
        if (root is TextBox textBox) { textBox.Text = UiLocalization.Text(languageCode, textBox.Text); textBox.FlowDirection = flowDirection; }
        if (root is ContentControl contentControl && contentControl.Content is string content)
        {
            var translated = UiLocalization.Text(languageCode, content);
            contentControl.Content = translated;
            contentControl.FlowDirection = flowDirection;
            if (contentControl is Button button && translated.Length > 12 && button.Width is > 0 and < 160)
                button.FontSize = Math.Min(button.FontSize, 10.5);
            if (contentControl is CheckBox checkBox && translated.Length > 34)
                checkBox.FontSize = Math.Min(checkBox.FontSize, 10.5);
        }
        if (root is HeaderedContentControl headered && headered.Header is string header)
            headered.Header = UiLocalization.Text(languageCode, header);
        if (ToolTipService.GetToolTip(root) is string tip)
            ToolTipService.SetToolTip(root, UiLocalization.Text(languageCode, tip));

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject) Apply(dependencyObject, languageCode);
            else if (child is IEnumerable enumerable)
                foreach (var item in enumerable)
                    if (item is DependencyObject nested) Apply(nested, languageCode);
        }
    }
}
