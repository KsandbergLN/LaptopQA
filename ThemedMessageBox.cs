using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LaptopQA.Windows;

// Shadows System.Windows.MessageBox inside the app so ordinary prompts keep the Laptop QA visual language.
internal static class MessageBox
{
	public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
	{
		string theme = (Application.Current?.MainWindow as MainWindow)?.ActiveTheme ?? "Light";
		return Show(owner, messageBoxText, caption, button, icon, theme);
	}

	public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, string theme)
	{
		bool isLight = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
		bool isAmoled = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
		Brush shell = Brush(isLight ? "#FFFAFAF6" : (isAmoled ? "#FF080808" : "#FF24414B"));
		Brush panel = Brush(isLight ? "#FFEAF0EF" : (isAmoled ? "#FF151515" : "#FF1D3038"));
		Brush text = Brush(isLight ? "#FF102A33" : (isAmoled ? "#FFF4F4F4" : "#FFF3F7F8"));
		Brush muted = Brush(isLight ? "#FF39515A" : (isAmoled ? "#FFBDBDBD" : "#FFC9E2E8"));
		Brush border = Brush(isLight ? "#FF8CA2A8" : (isAmoled ? "#FF5B5B5B" : "#FF789AA4"));
		Brush primary = Brush(isAmoled ? "#FFE0E0E0" : "#FFA2E6DD");
		Brush primaryText = Brush(isAmoled ? "#FF050505" : "#FF073F55");
		Brush secondary = Brush(isLight ? "#FFD8E1DF" : (isAmoled ? "#FF303030" : "#FF485D66"));
		Brush secondaryText = Brush(isLight ? "#FF17313A" : "#FFFFFFFF");
		Brush iconBrush = icon switch
		{
			MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => Brush(isAmoled ? "#FFB0B0B0" : "#FFD26161"),
			MessageBoxImage.Warning or MessageBoxImage.Exclamation => Brush(isAmoled ? "#FFD0D0D0" : "#FFF2C75B"),
			MessageBoxImage.Question => Brush(isAmoled ? "#FFD0D0D0" : "#FF8FD3E0"),
			_ => Brush(isAmoled ? "#FFD0D0D0" : "#FFA2E6DD")
		};
		string iconText = icon switch
		{
			MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => "!",
			MessageBoxImage.Warning or MessageBoxImage.Exclamation => "!",
			MessageBoxImage.Question => "?",
			_ => "i"
		};

		Window dialog = new()
		{
			Title = caption,
			Width = 500,
			SizeToContent = SizeToContent.Height,
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			Owner = owner,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ShowInTaskbar = false,
			Topmost = owner.Topmost
		};

		Border card = new()
		{
			Margin = new Thickness(18),
			Padding = new Thickness(26, 22, 26, 20),
			CornerRadius = new CornerRadius(20),
			Background = shell,
			BorderBrush = border,
			BorderThickness = new Thickness(1),
			Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = isLight ? 0.27 : 0.38, Color = Colors.Black }
		};
		dialog.Content = card;
		Grid layout = new();
		layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		card.Child = layout;

		Grid body = new();
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		Border glyph = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = iconBrush, VerticalAlignment = VerticalAlignment.Top };
		glyph.Child = new TextBlock { Text = iconText, Foreground = Brush("#FF102A2D"), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		body.Children.Add(glyph);
		TextBlock content = new() { Text = messageBoxText, Foreground = text, FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(content, 1);
		body.Children.Add(content);
		layout.Children.Add(body);

		StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
		Grid.SetRow(buttons, 1);
		layout.Children.Add(buttons);
		MessageBoxResult result = button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
		if (button == MessageBoxButton.YesNo)
		{
			buttons.Children.Add(CreateButton("No", secondary, secondaryText, border, () => { result = MessageBoxResult.No; dialog.Close(); }, true));
			buttons.Children.Add(CreateButton("Yes", primary, primaryText, border, () => { result = MessageBoxResult.Yes; dialog.Close(); }, false));
		}
		else
		{
			buttons.Children.Add(CreateButton("OK", primary, primaryText, border, () => { result = MessageBoxResult.OK; dialog.Close(); }, false));
		}

		dialog.Closed += (_, _) => { };
		dialog.ShowDialog();
		return result;
	}

	private static Button CreateButton(string label, Brush background, Brush foreground, Brush border, Action clicked, bool isCancel)
	{
		Button button = new()
		{
			Content = label,
			Width = 92,
			Height = 34,
			Margin = new Thickness(8, 0, 0, 0),
			Background = background,
			Foreground = foreground,
			BorderBrush = border,
			BorderThickness = new Thickness(1),
			FontWeight = FontWeights.SemiBold,
			Cursor = Cursors.Hand,
			IsCancel = isCancel,
			IsDefault = !isCancel,
			Template = ButtonChrome.RoundedTemplate()
		};
		button.Click += (_, _) => clicked();
		return button;
	}

	private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
