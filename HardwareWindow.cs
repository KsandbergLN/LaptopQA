using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LaptopQA.Windows;

public sealed class HardwareWindow : Window
{
	public HardwareWindow(Window owner, string text, string serial, string outputDir, string theme, string languageCode, Action<string>? saved = null, string titleText = "Hardware Details", bool showSave = true)
	{
		HardwareWindow hardwareWindow = this;
		base.Owner = owner;
		base.Title = titleText;
		base.Width = 760.0;
		base.Height = 650.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.NoResize;
		base.AllowsTransparency = true;
		base.Background = Brushes.Transparent;
		base.FontFamily = new FontFamily("Segoe UI");
		bool flag = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
		string hex = (flag ? "#FAFAF6" : (flag2 ? "#F0000000" : "#E0374F59"));
		string hex2 = (flag ? "#F0F1EC" : (flag2 ? "#F0080808" : "#E0162D38"));
		string hex3 = (flag ? "#9BAFB5" : (flag2 ? "#5A5A5A" : "#77D6F6FF"));
		Brush brush = BrushFromHex(flag ? "#06141B" : (flag2 ? "#F4F4F4" : "#F8FAFC"));
		Brush foreground = BrushFromHex(flag ? "#1D323C" : (flag2 ? "#BDBDBD" : "#C9E2E8"));
		Brush background = BrushFromHex(flag ? "#FFFAFAF6" : (flag2 ? "#FF080808" : "#24414B"));
		Brush borderBrush = BrushFromHex(flag ? "#78909A" : (flag2 ? "#666666" : "#6682949B"));
		Brush background2 = BrushFromHex(flag ? "#D8E1DF" : (flag2 ? "#303030" : "#485D66"));
		Color color = ColorFromHex(flag ? "#657A80" : (flag2 ? "#000000" : "#002E3A"));
		Canvas canvas = (Canvas)(base.Content = new Canvas());
		Border shell = new Border
		{
			Width = 740.0,
			Height = 630.0,
			Margin = new Thickness(10.0),
			CornerRadius = new CornerRadius(24.0),
			BorderBrush = BrushFromHex(hex3),
			BorderThickness = new Thickness(1.0),
			Background = new LinearGradientBrush(ColorFromHex(hex), ColorFromHex(hex2), new Point(0.0, 0.0), new Point(1.0, 1.0)),
			Effect = new DropShadowEffect
			{
				BlurRadius = 28.0,
				ShadowDepth = 0.0,
				Opacity = (flag ? 0.3 : (flag2 ? 0.54 : 0.38)),
				Color = color
			}
		};
		canvas.Children.Add(shell);
		Canvas canvas3 = new Canvas();
		shell.Child = canvas3;
		TextBlock textBlock = new TextBlock
		{
			Text = titleText,
			Width = 530.0,
			Height = 34.0,
			Foreground = brush,
			FontSize = 24.0,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		SetCanvas(textBlock, 34.0, 26.0);
		textBlock.MouseLeftButtonDown += delegate
		{
			try
			{
				hardwareWindow.DragMove();
			}
			catch
			{
			}
		};
		canvas3.Children.Add(textBlock);
		Button button = DialogButton("Save", background2, brush, 78.0);
		button.ToolTip = "Save hardware details to a text file.";
		SetCanvas(button, 598.0, 26.0);
		if (showSave)
		{
			canvas3.Children.Add(button);
		}
		Button button2 = DialogButton("X", background2, brush, 38.0);
		button2.ToolTip = "Close details.";
		SetCanvas(button2, 686.0, 26.0);
		canvas3.Children.Add(button2);
		bool flag3 = string.Equals(titleText, "Diagnostics Log", StringComparison.OrdinalIgnoreCase);
		TextBox? search = null;
		Button? button3 = null;
		if (flag3)
		{
			TextBlock element = new TextBlock
			{
				Text = "Search log:",
				Width = 78.0,
				Height = 34.0,
				Foreground = brush,
				FontSize = 12.0,
				FontWeight = FontWeights.SemiBold,
				TextAlignment = TextAlignment.Left,
				Padding = new Thickness(0.0, 8.0, 0.0, 0.0),
				ToolTip = "Type here to filter the diagnostics log."
			};
			SetCanvas(element, 34.0, 78.0);
			canvas3.Children.Add(element);
			search = new TextBox
			{
				Width = 470.0,
				Height = 34.0,
				Background = background,
				Foreground = brush,
				BorderBrush = borderBrush,
				BorderThickness = new Thickness(1.0),
				CaretBrush = brush,
				FontSize = 12.0,
				Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
				ToolTip = "Type here to filter the diagnostics log."
			};
			SetCanvas(search, 116.0, 78.0);
			canvas3.Children.Add(search);
			button3 = DialogButton("Clear", background2, brush, 110.0);
			button3.ToolTip = "Clear diagnostics log search.";
			SetCanvas(button3, 596.0, 78.0);
			canvas3.Children.Add(button3);
		}
		TextBox box = new TextBox
		{
			Text = text,
			Width = 672.0,
			Height = (flag3 ? 446 : 490),
			Background = background,
			Foreground = brush,
			BorderBrush = borderBrush,
			BorderThickness = new Thickness(1.0),
			CaretBrush = brush,
			SelectionBrush = BrushFromHex(flag ? "#2F6F68" : "#A2E6DD"),
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.0,
			Padding = new Thickness(10.0),
			IsReadOnly = true,
			TextWrapping = TextWrapping.Wrap,
			AcceptsReturn = true,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};
		SetCanvas(box, 34.0, flag3 ? 122 : 78);
		canvas3.Children.Add(box);
		if (search != null && button3 != null)
		{
			search.TextChanged += delegate
			{
				ApplySearch();
			};
			search.KeyDown += delegate(object _, KeyEventArgs e)
			{
				if (e.Key == Key.Escape)
				{
					search.Clear();
					e.Handled = true;
				}
			};
			button3.Click += delegate
			{
				search.Clear();
				search.Focus();
			};
		}
		TextBlock element2 = new TextBlock
		{
			Text = "Select any text and press Ctrl+C to copy.",
			Width = 672.0,
			Height = 24.0,
			Foreground = foreground,
			FontSize = 12.5
		};
		SetCanvas(element2, 34.0, 586.0);
		canvas3.Children.Add(element2);
		shell.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.OriginalSource == shell)
			{
				try
				{
					hardwareWindow.DragMove();
				}
				catch
				{
				}
			}
		};
		button2.Click += delegate
		{
			hardwareWindow.Close();
		};
		button.Click += delegate
		{
			Directory.CreateDirectory(outputDir);
			string value = Regex.Replace(string.IsNullOrWhiteSpace(serial) ? "unknown" : serial, "[<>:\"/\\\\|?*\\x00-\\x1f]+", "-");
			string text2 = Path.Combine(outputDir, $"{DateTime.Now:yyyy-MM-dd-HHmmss}-{value}-Hardware.txt");
			File.WriteAllText(text2, text);
			saved?.Invoke(text2);
			MessageBox.Show(hardwareWindow, "Hardware details saved:\n" + text2, "Hardware Details", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		};
		WpfLocalization.Apply(this, languageCode);
		void ApplySearch()
		{
			string query = search.Text;
			box.Text = (string.IsNullOrWhiteSpace(query) ? text : string.Join(Environment.NewLine, from line in text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None)
				where line.Contains(query, StringComparison.OrdinalIgnoreCase)
				select line));
			box.ScrollToHome();
		}
	}

	private static Button DialogButton(string text, Brush background, Brush foreground, double width)
	{
		return new Button
		{
			Content = text,
			Width = width,
			Height = 34.0,
			Foreground = foreground,
			Background = background,
			BorderThickness = new Thickness(0.0),
			FontWeight = FontWeights.Bold,
			Cursor = Cursors.Hand,
			Padding = new Thickness(0.0),
			Template = ButtonChrome.RoundedTemplate()
		};
	}

	private static void SetCanvas(FrameworkElement element, double left, double top)
	{
		Canvas.SetLeft(element, left);
		Canvas.SetTop(element, top);
	}

	private static Brush BrushFromHex(string hex)
	{
		return new SolidColorBrush(ColorFromHex(hex));
	}

	private static Color ColorFromHex(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.Transparent));
	}
}
