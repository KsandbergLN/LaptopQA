using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaptopQA.Windows;

internal sealed class TransientNotificationWindow : Window
{
	public TransientNotificationWindow(Window owner, string message, string theme)
	{
		Owner = owner;
		ShowInTaskbar = false;
		ShowActivated = false;
		Topmost = true;
		WindowStyle = WindowStyle.None;
		ResizeMode = ResizeMode.NoResize;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		Width = 500;
		Height = 92;

		(bool isDark, Color background, Color border, Color foreground, Color accent) = ThemeColors(theme);
		Border card = new Border
		{
			Background = new SolidColorBrush(background),
			BorderBrush = new SolidColorBrush(border),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(16),
			Padding = new Thickness(18, 16, 18, 16),
			Effect = new System.Windows.Media.Effects.DropShadowEffect
			{
				BlurRadius = 18,
				ShadowDepth = 4,
				Opacity = isDark ? 0.4 : 0.22,
				Color = Colors.Black
			}
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		Border accentBar = new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(4) };
		TextBlock messageText = new TextBlock
		{
			Text = message,
			Foreground = new SolidColorBrush(foreground),
			FontFamily = new FontFamily("Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			FontSize = 14,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(12, 0, 0, 0)
		};
		Grid.SetColumn(messageText, 1);
		grid.Children.Add(accentBar);
		grid.Children.Add(messageText);
		card.Child = grid;
		Content = card;

		Loaded += (_, _) =>
		{
			double desiredLeft = owner.Left + ((owner.ActualWidth - Width) / 2);
			double desiredTop = owner.Top + ((owner.ActualHeight - Height) / 2);
			Left = Math.Clamp(desiredLeft, SystemParameters.WorkArea.Left + 12, SystemParameters.WorkArea.Right - Width - 12);
			Top = Math.Clamp(desiredTop, SystemParameters.WorkArea.Top + 12, SystemParameters.WorkArea.Bottom - Height - 12);
			DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				Close();
			};
			timer.Start();
		};
	}

	private static (bool IsDark, Color Background, Color Border, Color Text, Color Accent) ThemeColors(string theme)
	{
		return theme switch
		{
			"Amoled" => (true, Color.FromRgb(16, 16, 16), Color.FromRgb(83, 83, 83), Color.FromRgb(245, 245, 245), Color.FromRgb(175, 175, 175)),
			"Dark" => (true, Color.FromRgb(38, 61, 70), Color.FromRgb(101, 130, 139), Color.FromRgb(243, 247, 248), Color.FromRgb(125, 205, 190)),
			_ => (false, Color.FromRgb(248, 250, 249), Color.FromRgb(157, 179, 185), Color.FromRgb(19, 37, 45), Color.FromRgb(18, 99, 61))
		};
	}
}
