using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LaptopQA.Windows;

public sealed class TechnicianNameWindow : Window
{
	private readonly TextBox _name = new TextBox();

	public string TechnicianName => _name.Text.Trim();

	public TechnicianNameWindow(Window owner, string theme, string languageCode)
	{
		base.Owner = owner;
		base.Title = "Laptop QA Onboarding";
		base.Width = 430.0;
		base.Height = 220.0;
		base.ResizeMode = ResizeMode.NoResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.ShowInTaskbar = false;
		bool flag = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
		base.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(flag ? "#F4F7F6" : (flag2 ? "#000000" : "#203C46")));
		base.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(flag ? "#102633" : (flag2 ? "#F4F4F4" : "#F3F7F8")));
		StackPanel stackPanel = (StackPanel)(base.Content = new StackPanel
		{
			Margin = new Thickness(28.0, 22.0, 28.0, 20.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Welcome to Laptop QA",
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Enter the technician name to use on QA sheets and app records.",
			FontSize = 13.0,
			TextWrapping = TextWrapping.Wrap,
			Opacity = 0.88,
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
		});
		_name.Height = 32.0;
		_name.FontSize = 14.0;
		_name.Margin = new Thickness(0.0, 0.0, 0.0, 18.0);
		_name.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				SaveAndClose();
			}
		};
		stackPanel.Children.Add(_name);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button button = new Button
		{
			Content = "Save",
			Width = 96.0,
			Height = 34.0,
			FontWeight = FontWeights.Bold,
			IsDefault = true,
			Template = ButtonChrome.RoundedTemplate()
		};
		button.Click += delegate
		{
			SaveAndClose();
		};
		stackPanel2.Children.Add(button);
		stackPanel.Children.Add(stackPanel2);
		base.Loaded += delegate
		{
			_name.Focus();
		};
		WpfLocalization.Apply(this, languageCode);
	}

	private void SaveAndClose()
	{
		if (string.IsNullOrWhiteSpace(_name.Text))
		{
			MessageBox.Show(this, "Please enter the technician name.", "Laptop QA", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			base.DialogResult = true;
		}
	}
}
