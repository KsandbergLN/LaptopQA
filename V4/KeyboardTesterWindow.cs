using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LaptopQATestingV4;

public sealed class KeyboardTesterWindow : Window
{
	private sealed record KeyboardTesterPalette(string ShellStart, string ShellEnd, string Border, string Text, string Eyebrow, string Panel, string Metric, string MetricLabel, string Reset, string ResetText, string ProgressShell, string ProgressStart, string ProgressEnd, string Key, string KeyText, string KeyStroke, string Tested, string TestedText, string Active, string ActiveText, string Shadow, double ShadowOpacity)
	{
		public static KeyboardTesterPalette For(string theme)
		{
			if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
			{
				return new KeyboardTesterPalette("#FAFAF6", "#E3E6E0", "#7F969F", "#06141B", "#004F4A", "#FFFFFFFF", "#D8E1DF", "#1D323C", "#60757E", "#FFFFFF", "#EAF0EF", "#22C55E", "#9B3036", "#F8FAF6", "#102633", "#C7D5D7", "#22C55E", "#06141B", "#9B3036", "#FFFFFF", "#657A80", 0.22);
			}
			if (string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase))
			{
				return new KeyboardTesterPalette("#000000", "#080808", "#5A5A5A", "#F4F4F4", "#BDBDBD", "#050505", "#111111", "#BDBDBD", "#303030", "#F4F4F4", "#050505", "#DADADA", "#777777", "#1A1A1A", "#F4F4F4", "#555555", "#DADADA", "#050505", "#777777", "#F4F4F4", "#000000", 0.54);
			}
			return new KeyboardTesterPalette("#415D66", "#102730", "#6C8F98", "#FFFFFF", "#75C9D9", "#112B34", "#354F59", "#C9E2E8", "#354F59", "#FFFFFF", "#112B34", "#56D5A8", "#F08A6D", "#E4EFF1", "#16313A", "#D7E7EB", "#56D5A8", "#073A2B", "#F08A6D", "#FFFFFF", "#243941", 0.24);
		}
	}

	private sealed record KeyDefinition(Key Key, string Label, int X, int Y, int W = 2, int H = 1);

	private readonly Dictionary<Key, List<Border>> _keyControls = new Dictionary<Key, List<Border>>();

	private readonly Dictionary<Key, string> _keyLabels = new Dictionary<Key, string>();

	private readonly Dictionary<Border, TextBlock> _keyTextBlocks = new Dictionary<Border, TextBlock>();

	private readonly HashSet<Key> _tested = new HashSet<Key>();

	private readonly HashSet<Key> _active = new HashSet<Key>();

	public string PressedKeysSummary
	{
		get
		{
			List<Key> orderedKeys = KeyDefinitions()
				.Select((KeyDefinition definition) => definition.Key)
				.Distinct()
				.Where((Key key) => _tested.Contains(key))
				.ToList();
			return orderedKeys.Count == 0
				? "None"
				: string.Join(", ", orderedKeys.Select(ActivityLabel));
		}
	}

	private readonly TextBlock _testedCountText = new TextBlock();

	private readonly TextBlock _activeCountText = new TextBlock();

	private readonly TextBlock _lastKeyText = new TextBlock();

	private readonly TextBlock _activeKeysText = new TextBlock();

	private readonly Border _progressBar = new Border();

	private readonly Brush _windowBorderBrush;

	private readonly Brush _windowTextBrush;

	private readonly Brush _eyebrowBrush;

	private readonly Brush _panelBrush;

	private readonly Brush _metricBrush;

	private readonly Brush _metricLabelBrush;

	private readonly Brush _resetBrush;

	private readonly Brush _resetTextBrush;

	private readonly Brush _progressShellBrush;

	private readonly Brush _keyBrush;

	private readonly Brush _keyTextBrush;

	private readonly Brush _keyStrokeBrush;

	private readonly Brush _testedBrush;

	private readonly Brush _testedTextBrush;

	private readonly Brush _activeBrush;

	private readonly Brush _activeTextBrush;

	private readonly Color _shellStartColor;

	private readonly Color _shellEndColor;

	private readonly Color _progressStartColor;

	private readonly Color _progressEndColor;

	private readonly Color _shadowColor;

	private readonly double _shadowOpacity;

	public KeyboardTesterWindow(Window owner, string theme, string languageCode)
	{
		KeyboardTesterPalette keyboardTesterPalette = KeyboardTesterPalette.For(theme);
		_windowBorderBrush = BrushFromHex(keyboardTesterPalette.Border);
		_windowTextBrush = BrushFromHex(keyboardTesterPalette.Text);
		_eyebrowBrush = BrushFromHex(keyboardTesterPalette.Eyebrow);
		_panelBrush = BrushFromHex(keyboardTesterPalette.Panel);
		_metricBrush = BrushFromHex(keyboardTesterPalette.Metric);
		_metricLabelBrush = BrushFromHex(keyboardTesterPalette.MetricLabel);
		_resetBrush = BrushFromHex(keyboardTesterPalette.Reset);
		_resetTextBrush = BrushFromHex(keyboardTesterPalette.ResetText);
		_progressShellBrush = BrushFromHex(keyboardTesterPalette.ProgressShell);
		_keyBrush = BrushFromHex(keyboardTesterPalette.Key);
		_keyTextBrush = BrushFromHex(keyboardTesterPalette.KeyText);
		_keyStrokeBrush = BrushFromHex(keyboardTesterPalette.KeyStroke);
		_testedBrush = BrushFromHex(keyboardTesterPalette.Tested);
		_testedTextBrush = BrushFromHex(keyboardTesterPalette.TestedText);
		_activeBrush = BrushFromHex(keyboardTesterPalette.Active);
		_activeTextBrush = BrushFromHex(keyboardTesterPalette.ActiveText);
		_shellStartColor = ColorFromHex(keyboardTesterPalette.ShellStart);
		_shellEndColor = ColorFromHex(keyboardTesterPalette.ShellEnd);
		_progressStartColor = ColorFromHex(keyboardTesterPalette.ProgressStart);
		_progressEndColor = ColorFromHex(keyboardTesterPalette.ProgressEnd);
		_shadowColor = ColorFromHex(keyboardTesterPalette.Shadow);
		_shadowOpacity = keyboardTesterPalette.ShadowOpacity;
		base.Owner = owner;
		base.Title = "Kris's Keyboard Tester";
		base.Width = 1240.0;
		base.Height = 740.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.NoResize;
		base.AllowsTransparency = true;
		base.Background = Brushes.Transparent;
		base.Foreground = _windowTextBrush;
		base.Focusable = true;
		Canvas canvas = (Canvas)(base.Content = new Canvas());
		Border border = new Border
		{
			Width = 1240.0,
			Height = 740.0,
			CornerRadius = new CornerRadius(18.0),
			BorderBrush = _windowBorderBrush,
			BorderThickness = new Thickness(1.0),
			Background = new LinearGradientBrush(_shellStartColor, _shellEndColor, new Point(0.0, 0.0), new Point(1.0, 1.0)),
			Effect = new DropShadowEffect
			{
				BlurRadius = 24.0,
				ShadowDepth = 10.0,
				Opacity = _shadowOpacity,
				Color = _shadowColor
			}
		};
		canvas.Children.Add(border);
		Canvas canvas3 = (Canvas)(border.Child = new Canvas());
		Border border2 = new Border
		{
			Width = 1188.0,
			Height = 92.0,
			Background = Brushes.Transparent
		};
		border2.MouseLeftButtonDown += delegate
		{
			try
			{
				DragMove();
			}
			catch
			{
			}
		};
		canvas3.Children.Add(border2);
		Button button = new Button
		{
			Content = "X",
			Width = 24.0,
			Height = 24.0,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Foreground = _windowTextBrush,
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Cursor = Cursors.Hand,
			Padding = new Thickness(0.0),
			Template = V4ButtonChrome.RoundedTemplate()
		};
		button.Click += delegate
		{
			Close();
		};
		SetCanvas(button, 1200.0, 8.0);
		canvas3.Children.Add(button);
		canvas3.Children.Add(Text("LAPTOP QA TESTING", 54.0, 36.0, 220.0, 22.0, 13.0, _eyebrowBrush, FontWeights.ExtraBold));
		canvas3.Children.Add(Text("Kris's Keyboard Tester", 54.0, 62.0, 720.0, 58.0, 44.0, _windowTextBrush, FontWeights.ExtraBold));
		Border element = MetricCard("TESTED", _testedCountText);
		SetCanvas(element, 876.0, 36.0);
		canvas3.Children.Add(element);
		Border element2 = MetricCard("ACTIVE", _activeCountText);
		SetCanvas(element2, 986.0, 36.0);
		canvas3.Children.Add(element2);
		Button button2 = new Button
		{
			Content = "Reset",
			Width = 86.0,
			Height = 74.0,
			Background = _resetBrush,
			BorderBrush = _windowBorderBrush,
			BorderThickness = new Thickness(1.0),
			Foreground = _resetTextBrush,
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Cursor = Cursors.Hand,
			Template = V4ButtonChrome.RoundedTemplate()
		};
		button2.Click += delegate
		{
			Reset();
		};
		SetCanvas(button2, 1096.0, 36.0);
		canvas3.Children.Add(button2);
		Border element3 = InfoPanel("LAST KEY", _lastKeyText);
		SetCanvas(element3, 54.0, 130.0);
		canvas3.Children.Add(element3);
		Border element4 = InfoPanel("ACTIVE KEYS", _activeKeysText, 562.0);
		SetCanvas(element4, 620.0, 130.0);
		canvas3.Children.Add(element4);
		Border border3 = new Border
		{
			Width = 1128.0,
			Height = 12.0,
			CornerRadius = new CornerRadius(7.0),
			BorderBrush = _windowBorderBrush,
			BorderThickness = new Thickness(1.0),
			Background = _progressShellBrush
		};
		Canvas canvas4 = (Canvas)(border3.Child = new Canvas());
		_progressBar.Width = 0.0;
		_progressBar.Height = 10.0;
		_progressBar.CornerRadius = new CornerRadius(7.0);
		_progressBar.Background = new LinearGradientBrush(_progressStartColor, _progressEndColor, new Point(0.0, 0.0), new Point(1.0, 0.0));
		canvas4.Children.Add(_progressBar);
		SetCanvas(border3, 54.0, 230.0);
		canvas3.Children.Add(border3);
		Border border4 = RoundedPanel(1128.0, 430.0, _panelBrush, 16.0);
		Canvas canvas5 = (Canvas)(border4.Child = new Canvas());
		SetCanvas(border4, 54.0, 270.0);
		canvas3.Children.Add(border4);
		foreach (KeyDefinition item in KeyDefinitions())
		{
			Border border5 = CreateKey(item);
			canvas5.Children.Add(border5);
			if (!_keyControls.TryGetValue(item.Key, out List<Border>? value))
			{
				value = new List<Border>();
				_keyControls[item.Key] = value;
				_keyLabels[item.Key] = item.Label;
			}
			value.Add(border5);
		}
		base.PreviewKeyDown += OnKeyDown;
		base.PreviewKeyUp += OnKeyUp;
		base.Deactivated += delegate
		{
			_active.Clear();
			Render();
		};
		base.ContentRendered += delegate
		{
			Activate();
			Focus();
		};
		Render();
		WpfLocalization.Apply(this, languageCode);
	}

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		Key key = NormalizeKey(e);
		_lastKeyText.Text = $"{(_keyLabels.TryGetValue(key, out string? value) ? value : Label(key))} ({key})";
		_active.Add(key);
		_tested.Add(key);
		Render();
		e.Handled = true;
	}

	private void OnKeyUp(object sender, KeyEventArgs e)
	{
		Key item = NormalizeKey(e);
		_active.Remove(item);
		Render();
		e.Handled = true;
	}

	private void Render()
	{
		foreach (var (item, list2) in _keyControls)
		{
			foreach (Border item2 in list2)
			{
				TextBlock textBlock = _keyTextBlocks[item2];
				if (_active.Contains(item))
				{
					item2.Background = _activeBrush;
					item2.BorderBrush = _activeBrush;
					textBlock.Foreground = _activeTextBrush;
				}
				else if (_tested.Contains(item))
				{
					item2.Background = _testedBrush;
					item2.BorderBrush = _testedBrush;
					textBlock.Foreground = _testedTextBrush;
				}
				else
				{
					item2.Background = _keyBrush;
					item2.BorderBrush = _keyStrokeBrush;
					textBlock.Foreground = _keyTextBrush;
				}
			}
		}
		_testedCountText.Text = _tested.Count.ToString(CultureInfo.InvariantCulture);
		_activeCountText.Text = _active.Count.ToString(CultureInfo.InvariantCulture);
		_activeKeysText.Text = ((_active.Count == 0) ? "None" : string.Join(" + ", _active.Select((Key k) => (!_keyLabels.TryGetValue(k, out string? value)) ? Label(k) : value)));
		_progressBar.Width = ((_keyControls.Count == 0) ? 0.0 : Math.Round(1126.0 * ((double)_tested.Count / (double)_keyControls.Count), 0));
	}

	private void Reset()
	{
		_tested.Clear();
		_active.Clear();
		_lastKeyText.Text = "Waiting";
		Render();
	}

	private Border CreateKey(KeyDefinition definition)
	{
		double left = 16.0 + (double)(definition.X - 1) * 22.0;
		double top = 18.0 + (double)(definition.Y - 1) * 65.0;
		double width = (double)definition.W * 16.0 + (double)(definition.W - 1) * 6.0;
		double height = (double)definition.H * 46.0 + (double)(definition.H - 1) * 19.0;
		Border border = RoundedPanel(width, height, _keyBrush, 8.0);
		border.BorderBrush = _keyStrokeBrush;
		SetCanvas(border, left, top);
		TextBlock value = (TextBlock)(border.Child = new TextBlock
		{
			Text = definition.Label,
			Foreground = _keyTextBrush,
			FontSize = 14.0,
			FontWeight = FontWeights.ExtraBold,
			TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		});
		_keyTextBlocks[border] = value;
		return border;
	}

	private Border MetricCard(string label, TextBlock count)
	{
		Border border = RoundedPanel(96.0, 74.0, _metricBrush, 14.0);
		Canvas canvas = (Canvas)(border.Child = new Canvas());
		count.Text = "0";
		count.TextAlignment = TextAlignment.Center;
		count.Foreground = _windowTextBrush;
		count.FontSize = 28.0;
		count.FontWeight = FontWeights.ExtraBold;
		SetCanvas(count, 0.0, 12.0, 96.0, 34.0);
		canvas.Children.Add(count);
		TextBlock textBlock = Text(label, 0.0, 48.0, 96.0, 18.0, 13.0, _metricLabelBrush, FontWeights.ExtraBold);
		textBlock.TextAlignment = TextAlignment.Center;
		canvas.Children.Add(textBlock);
		return border;
	}

	private Border InfoPanel(string label, TextBlock value, double width = 550.0)
	{
		Border border = RoundedPanel(width, 84.0, _panelBrush, 14.0);
		Canvas canvas = (Canvas)(border.Child = new Canvas());
		canvas.Children.Add(Text(label, 16.0, 16.0, 220.0, 22.0, 14.0, _metricLabelBrush, FontWeights.ExtraBold));
		value.Text = ((label == "LAST KEY") ? "Waiting" : "None");
		value.Foreground = _windowTextBrush;
		value.FontSize = 22.0;
		value.FontWeight = FontWeights.ExtraBold;
		value.TextTrimming = TextTrimming.CharacterEllipsis;
		SetCanvas(value, 16.0, 42.0, width - 40.0, 30.0);
		canvas.Children.Add(value);
		return border;
	}

	private Border RoundedPanel(double width, double height, Brush background, double radius)
	{
		return new Border
		{
			Width = width,
			Height = height,
			CornerRadius = new CornerRadius(radius),
			Background = background,
			BorderBrush = _windowBorderBrush,
			BorderThickness = new Thickness(1.0)
		};
	}

	private static TextBlock Text(string text, double left, double top, double width, double height, double fontSize, Brush foreground, FontWeight weight)
	{
		TextBlock obj = new TextBlock
		{
			Text = text,
			Foreground = foreground,
			FontSize = fontSize,
			FontWeight = weight,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		SetCanvas(obj, left, top, width, height);
		return obj;
	}

	private static void SetCanvas(FrameworkElement element, double left, double top)
	{
		Canvas.SetLeft(element, left);
		Canvas.SetTop(element, top);
	}

	private static void SetCanvas(FrameworkElement element, double left, double top, double width, double height)
	{
		element.Width = width;
		element.Height = height;
		SetCanvas(element, left, top);
	}

	private static Key NormalizeKey(KeyEventArgs e)
	{
		return e.Key switch
		{
			Key.System => e.SystemKey, 
			Key.ImeProcessed => e.ImeProcessedKey, 
			_ => e.Key, 
		};
	}

	private static Brush BrushFromHex(string hex)
	{
		return new SolidColorBrush(ColorFromHex(hex));
	}

	private static Color ColorFromHex(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.Transparent));
	}

	private static IReadOnlyList<KeyDefinition> KeyDefinitions()
	{
		return new KeyDefinition[104]
		{
			new KeyDefinition(Key.Escape, "Esc", 1, 1),
			new KeyDefinition(Key.F1, "F1", 5, 1),
			new KeyDefinition(Key.F2, "F2", 7, 1),
			new KeyDefinition(Key.F3, "F3", 9, 1),
			new KeyDefinition(Key.F4, "F4", 11, 1),
			new KeyDefinition(Key.F5, "F5", 14, 1),
			new KeyDefinition(Key.F6, "F6", 16, 1),
			new KeyDefinition(Key.F7, "F7", 18, 1),
			new KeyDefinition(Key.F8, "F8", 20, 1),
			new KeyDefinition(Key.F9, "F9", 23, 1),
			new KeyDefinition(Key.F10, "F10", 25, 1),
			new KeyDefinition(Key.F11, "F11", 27, 1),
			new KeyDefinition(Key.F12, "F12", 29, 1),
			new KeyDefinition(Key.Snapshot, "PrtSc", 34, 1),
			new KeyDefinition(Key.Scroll, "ScrLk", 36, 1),
			new KeyDefinition(Key.Pause, "Pause", 38, 1),
			new KeyDefinition(Key.Oem3, "`", 1, 2),
			new KeyDefinition(Key.D1, "1", 3, 2),
			new KeyDefinition(Key.D2, "2", 5, 2),
			new KeyDefinition(Key.D3, "3", 7, 2),
			new KeyDefinition(Key.D4, "4", 9, 2),
			new KeyDefinition(Key.D5, "5", 11, 2),
			new KeyDefinition(Key.D6, "6", 13, 2),
			new KeyDefinition(Key.D7, "7", 15, 2),
			new KeyDefinition(Key.D8, "8", 17, 2),
			new KeyDefinition(Key.D9, "9", 19, 2),
			new KeyDefinition(Key.D0, "0", 21, 2),
			new KeyDefinition(Key.OemMinus, "-", 23, 2),
			new KeyDefinition(Key.OemPlus, "=", 25, 2),
			new KeyDefinition(Key.Back, "Backspace", 27, 2, 5),
			new KeyDefinition(Key.Insert, "Ins", 34, 2),
			new KeyDefinition(Key.Home, "Home", 36, 2),
			new KeyDefinition(Key.Prior, "PgUp", 38, 2),
			new KeyDefinition(Key.NumLock, "Num", 42, 2),
			new KeyDefinition(Key.Divide, "/", 44, 2),
			new KeyDefinition(Key.Multiply, "*", 46, 2),
			new KeyDefinition(Key.Subtract, "-", 48, 2),
			new KeyDefinition(Key.Tab, "Tab", 1, 3, 3),
			new KeyDefinition(Key.Q, "Q", 4, 3),
			new KeyDefinition(Key.W, "W", 6, 3),
			new KeyDefinition(Key.E, "E", 8, 3),
			new KeyDefinition(Key.R, "R", 10, 3),
			new KeyDefinition(Key.T, "T", 12, 3),
			new KeyDefinition(Key.Y, "Y", 14, 3),
			new KeyDefinition(Key.U, "U", 16, 3),
			new KeyDefinition(Key.I, "I", 18, 3),
			new KeyDefinition(Key.O, "O", 20, 3),
			new KeyDefinition(Key.P, "P", 22, 3),
			new KeyDefinition(Key.Oem4, "[", 24, 3),
			new KeyDefinition(Key.Oem6, "]", 26, 3),
			new KeyDefinition(Key.Oem5, "\\", 28, 3, 4),
			new KeyDefinition(Key.Delete, "Del", 34, 3),
			new KeyDefinition(Key.End, "End", 36, 3),
			new KeyDefinition(Key.Next, "PgDn", 38, 3),
			new KeyDefinition(Key.NumPad7, "7", 42, 3),
			new KeyDefinition(Key.NumPad8, "8", 44, 3),
			new KeyDefinition(Key.NumPad9, "9", 46, 3),
			new KeyDefinition(Key.Add, "+", 48, 3, 2, 2),
			new KeyDefinition(Key.Capital, "Caps", 1, 4, 4),
			new KeyDefinition(Key.A, "A", 5, 4),
			new KeyDefinition(Key.S, "S", 7, 4),
			new KeyDefinition(Key.D, "D", 9, 4),
			new KeyDefinition(Key.F, "F", 11, 4),
			new KeyDefinition(Key.G, "G", 13, 4),
			new KeyDefinition(Key.H, "H", 15, 4),
			new KeyDefinition(Key.J, "J", 17, 4),
			new KeyDefinition(Key.K, "K", 19, 4),
			new KeyDefinition(Key.L, "L", 21, 4),
			new KeyDefinition(Key.Oem1, ";", 23, 4),
			new KeyDefinition(Key.Oem7, "'", 25, 4),
			new KeyDefinition(Key.Return, "Enter", 27, 4, 5),
			new KeyDefinition(Key.NumPad4, "4", 42, 4),
			new KeyDefinition(Key.NumPad5, "5", 44, 4),
			new KeyDefinition(Key.NumPad6, "6", 46, 4),
			new KeyDefinition(Key.LeftShift, "Shift", 1, 5, 5),
			new KeyDefinition(Key.Z, "Z", 6, 5),
			new KeyDefinition(Key.X, "X", 8, 5),
			new KeyDefinition(Key.C, "C", 10, 5),
			new KeyDefinition(Key.V, "V", 12, 5),
			new KeyDefinition(Key.B, "B", 14, 5),
			new KeyDefinition(Key.N, "N", 16, 5),
			new KeyDefinition(Key.M, "M", 18, 5),
			new KeyDefinition(Key.OemComma, ",", 20, 5),
			new KeyDefinition(Key.OemPeriod, ".", 22, 5),
			new KeyDefinition(Key.Oem2, "/", 24, 5),
			new KeyDefinition(Key.RightShift, "Shift", 26, 5, 6),
			new KeyDefinition(Key.Up, "Up", 36, 5),
			new KeyDefinition(Key.NumPad1, "1", 42, 5),
			new KeyDefinition(Key.NumPad2, "2", 44, 5),
			new KeyDefinition(Key.NumPad3, "3", 46, 5),
			new KeyDefinition(Key.Return, "Enter", 48, 5, 2, 2),
			new KeyDefinition(Key.LeftCtrl, "Ctrl", 1, 6, 3),
			new KeyDefinition(Key.LWin, "Win", 4, 6, 3),
			new KeyDefinition(Key.LeftAlt, "Alt", 7, 6, 3),
			new KeyDefinition(Key.Space, "Space", 10, 6, 13),
			new KeyDefinition(Key.RightAlt, "Alt", 23, 6, 3),
			new KeyDefinition(Key.RWin, "Win", 26, 6),
			new KeyDefinition(Key.Apps, "Menu", 28, 6),
			new KeyDefinition(Key.RightCtrl, "Ctrl", 30, 6),
			new KeyDefinition(Key.Left, "Left", 34, 6),
			new KeyDefinition(Key.Down, "Down", 36, 6),
			new KeyDefinition(Key.Right, "Right", 38, 6),
			new KeyDefinition(Key.NumPad0, "0", 42, 6, 4),
			new KeyDefinition(Key.Decimal, ".", 46, 6)
		};
	}

	private static string Label(Key key)
	{
		switch (key)
		{
		case Key.Escape:
			return "Esc";
		case Key.Back:
			return "Backspace";
		case Key.Capital:
			return "Caps";
		case Key.LeftShift:
		case Key.RightShift:
			return "Shift";
		case Key.LeftCtrl:
		case Key.RightCtrl:
			return "Ctrl";
		case Key.LeftAlt:
		case Key.RightAlt:
			return "Alt";
		case Key.Space:
			return "Space";
		case Key.Oem3:
			return "`";
		case Key.OemMinus:
			return "-";
		case Key.OemPlus:
			return "=";
		case Key.Oem4:
			return "[";
		case Key.Oem6:
			return "]";
		case Key.Oem5:
			return "\\";
		case Key.Oem1:
			return ";";
		case Key.Oem7:
			return "'";
		case Key.OemComma:
			return ",";
		case Key.OemPeriod:
			return ".";
		case Key.Oem2:
			return "/";
		default:
			return key.ToString().Replace("D", "");
		}
	}

	private static string ActivityLabel(Key key)
	{
		return key switch
		{
			Key.LeftShift => "Left Shift",
			Key.RightShift => "Right Shift",
			Key.LeftCtrl => "Left Ctrl",
			Key.RightCtrl => "Right Ctrl",
			Key.LeftAlt => "Left Alt",
			Key.RightAlt => "Right Alt",
			Key.LWin => "Left Windows",
			Key.RWin => "Right Windows",
			Key.Return => "Enter",
			Key.Capital => "Caps Lock",
			Key.Snapshot => "Print Screen",
			Key.Scroll => "Scroll Lock",
			Key.Prior => "Page Up",
			Key.Next => "Page Down",
			_ => Label(key)
		};
	}
}
