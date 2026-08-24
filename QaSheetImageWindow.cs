using System;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Xps;

namespace LaptopQA.Windows;

public sealed class QaSheetImageWindow : Window
{
	private bool _printInProgress;

	public QaSheetImageWindow(Window owner, string imagePath, string theme, string languageCode, string serviceTag)
	{
		if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
		{
			throw new FileNotFoundException("QA sheet PNG was not found.", imagePath);
		}
		bool flag = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase);
		Rect workArea = SystemParameters.WorkArea;
		double width = Math.Min(860.0, Math.Max(660.0, workArea.Width - 90.0));
		double height = Math.Min(900.0, Math.Max(560.0, workArea.Height - 90.0));
		base.Owner = owner;
		base.Title = "QA Sheet";
		base.Width = width;
		base.Height = height;
		base.MinWidth = 560.0;
		base.MinHeight = 420.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.CanResizeWithGrip;
		base.AllowsTransparency = true;
		base.Background = Brushes.Transparent;
		base.ShowInTaskbar = false;
		base.UseLayoutRounding = true;
		base.SnapsToDevicePixels = true;
		Border shell = new Border
		{
			CornerRadius = new CornerRadius(18.0),
			Background = BrushFromHex(flag ? "#F7FAF8" : (flag2 ? "#000000" : "#233A44")),
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Effect = new DropShadowEffect
			{
				BlurRadius = 34.0,
				ShadowDepth = 8.0,
				Direction = 315.0,
				Opacity = (flag ? 0.22 : (flag2 ? 0.54 : 0.34)),
				Color = ColorFromHex("#000000")
			}
		};
		shell.SizeChanged += delegate
		{
			shell.Clip = new RectangleGeometry(new Rect(0.0, 0.0, shell.ActualWidth, shell.ActualHeight), 18.0, 18.0);
		};
		base.Content = shell;
		Grid grid = new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = new GridLength(48.0)
				},
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		shell.Child = grid;
		Grid grid2 = new Grid
		{
			Background = BrushFromHex(flag ? "#EAF1EF" : (flag2 ? "#111111" : "#2E4B55")),
			Cursor = Cursors.SizeAll
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(38.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(58.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(38.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(72.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(48.0)
		});
		grid2.MouseLeftButtonDown += delegate
		{
			try
			{
				DragMove();
			}
			catch
			{
			}
		};
		Grid.SetRow(grid2, 0);
		grid.Children.Add(grid2);
		TextBlock element = new TextBlock
		{
			Text = "QA Sheet Preview",
			Margin = new Thickness(18.0, 0.0, 10.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = BrushFromHex(flag ? "#102633" : (flag2 ? "#F4F4F4" : "#F3F7F8")),
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		grid2.Children.Add(element);
		SolidColorBrush foreground = BrushFromHex(flag ? "#102633" : (flag2 ? "#F4F4F4" : "#F3F7F8"));
		SolidColorBrush background = BrushFromHex(flag ? "#D8E4E1" : (flag2 ? "#303030" : "#3A5964"));
		TextBlock zoomText = new TextBlock
		{
			Text = "100%",
			Foreground = foreground,
			FontSize = 11.0,
			FontWeight = FontWeights.Bold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextAlignment = TextAlignment.Center
		};
		Grid.SetColumn(zoomText, 2);
		grid2.Children.Add(zoomText);
		Button zoomOut = PreviewHeaderButton("-", "Zoom out", foreground, background);
		Grid.SetColumn(zoomOut, 1);
		grid2.Children.Add(zoomOut);
		Button zoomIn = PreviewHeaderButton("+", "Zoom in", foreground, background);
		Grid.SetColumn(zoomIn, 3);
		grid2.Children.Add(zoomIn);
		Button button = PreviewHeaderButton("Print", "Print QA sheet", foreground, background, 64.0);
		Grid.SetColumn(button, 4);
		grid2.Children.Add(button);
		Button button2 = new Button
		{
			Content = PreviewCloseGlyph(foreground),
			Width = 36.0,
			Height = 32.0,
			Margin = new Thickness(0.0, 8.0, 10.0, 8.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Foreground = BrushFromHex(flag ? "#102633" : (flag2 ? "#F4F4F4" : "#F3F7F8")),
			Cursor = Cursors.Hand,
			ToolTip = "Close QA sheet preview",
			Template = ButtonChrome.RoundedTemplate()
		};
		button2.Click += delegate
		{
			Close();
		};
		Grid.SetColumn(button2, 5);
		grid2.Children.Add(button2);
		BitmapImage bitmap = LoadBitmap(imagePath);
		string printJobName = BuildPrintJobName(serviceTag);
		button.Click += delegate
		{
			if (_printInProgress)
			{
				return;
			}
			_printInProgress = true;
			button.IsEnabled = false;
			try
			{
				PrintQaSheet(this, bitmap, printJobName);
			}
			finally
			{
				_printInProgress = false;
				button.IsEnabled = true;
			}
		};
		ScaleTransform zoomScale = new ScaleTransform(1.0, 1.0);
		Image image = new Image
		{
			Source = bitmap,
			Width = ((bitmap.PixelWidth > 900) ? ((double)bitmap.PixelWidth / 2.0) : ((double)bitmap.PixelWidth)),
			Stretch = Stretch.Uniform,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(18.0),
			LayoutTransform = zoomScale
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
		double zoom = 1.0;
		zoomOut.Click += delegate
		{
			UpdateZoom(zoom - 0.1);
		};
		zoomIn.Click += delegate
		{
			UpdateZoom(zoom + 0.1);
		};
		UpdateZoom(1.0);
		ScrollViewer element2 = new ScrollViewer
		{
			Background = BrushFromHex(flag ? "#F7FAF8" : (flag2 ? "#000000" : "#233A44")),
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			CanContentScroll = false,
			Content = image
		};
		Grid.SetRow(element2, 1);
		grid.Children.Add(element2);
		base.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				Close();
			}
		};
		WpfLocalization.Apply(this, languageCode);
		void UpdateZoom(double value)
		{
			zoom = Math.Clamp(Math.Round(value * 10.0) / 10.0, 0.5, 2.5);
			zoomScale.ScaleX = zoom;
			zoomScale.ScaleY = zoom;
			zoomText.Text = $"{zoom * 100.0:0}%";
			zoomOut.IsEnabled = zoom > 0.5;
			zoomIn.IsEnabled = zoom < 2.5;
		}
	}

	private static BitmapImage LoadBitmap(string imagePath)
	{
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private static string BuildPrintJobName(string serviceTag)
	{
		string serial = (serviceTag ?? "").Trim().Replace("\r", "").Replace("\n", "");
		return string.IsNullOrWhiteSpace(serial) || string.Equals(serial, "Unknown", StringComparison.OrdinalIgnoreCase)
			? "Laptop QA"
			: "Laptop QA - " + serial;
	}

	private static void PrintQaSheet(Window owner, BitmapSource bitmap, string printJobName)
	{
		try
		{
			PrintDialog printDialog = new PrintDialog();
			// Start each QA print at one copy even when the selected printer saved a larger default.
			// The technician can still change the copy count in the dialog before printing.
			printDialog.PrintTicket.CopyCount = 1;
			printDialog.PrintTicket.OutputColor = OutputColor.Color;
			printDialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
			if (printDialog.ShowDialog() == true)
			{
				PrintQueue printQueue = printDialog.PrintQueue;
				printQueue.CurrentJobSettings.Description = printJobName;
				PrintTicket requestedTicket = printDialog.PrintTicket.Clone();
				int requestedCopies = Math.Max(1, requestedTicket.CopyCount ?? 1);
				requestedTicket.OutputColor = OutputColor.Color;
				requestedTicket.PageOrientation = PageOrientation.Portrait;
				PrintTicket validatedTicket = printQueue.MergeAndValidatePrintTicket(printQueue.DefaultPrintTicket, requestedTicket).ValidatedPrintTicket;
				// Preserve the copy count chosen in the dialog while forcing the standard color and portrait
				// features on the exact ticket that is submitted to the printer.
				validatedTicket.CopyCount = requestedCopies;
				validatedTicket.OutputColor = OutputColor.Color;
				validatedTicket.PageOrientation = PageOrientation.Portrait;
				PrintCapabilities printCapabilities = printQueue.GetPrintCapabilities(validatedTicket);
				PageImageableArea pageImageableArea = printCapabilities.PageImageableArea;
				double width = printCapabilities.OrientedPageMediaWidth ?? printDialog.PrintableAreaWidth;
				double height = printCapabilities.OrientedPageMediaHeight ?? printDialog.PrintableAreaHeight;
				double width2 = pageImageableArea?.ExtentWidth ?? printDialog.PrintableAreaWidth;
				double height2 = pageImageableArea?.ExtentHeight ?? printDialog.PrintableAreaHeight;
				double length = pageImageableArea?.OriginWidth ?? 0.0;
				double length2 = pageImageableArea?.OriginHeight ?? 0.0;
				double imageAspect = bitmap.PixelHeight > 0 ? (double)bitmap.PixelWidth / bitmap.PixelHeight : 1.0;
				double fittedWidth = width2;
				double fittedHeight = fittedWidth / imageAspect;
				if (fittedHeight > height2)
				{
					fittedHeight = height2;
					fittedWidth = fittedHeight * imageAspect;
				}
				double imageLeft = length + Math.Max(0.0, (width2 - fittedWidth) / 2.0);
				double imageTop = length2 + Math.Max(0.0, (height2 - fittedHeight) / 2.0);
				FixedPage fixedPage = new FixedPage
				{
					Width = width,
					Height = height,
					Background = Brushes.White
				};
				Image element = new Image
				{
					Source = bitmap,
					Width = fittedWidth,
					Height = fittedHeight,
					Stretch = Stretch.Uniform
				};
				FixedPage.SetLeft(element, imageLeft);
				FixedPage.SetTop(element, imageTop);
				fixedPage.Children.Add(element);
				fixedPage.Measure(new Size(width, height));
				fixedPage.Arrange(new Rect(0.0, 0.0, width, height));
				fixedPage.UpdateLayout();
				XpsDocumentWriter writer = PrintQueue.CreateXpsDocumentWriter(printQueue);
				writer.Write(fixedPage, validatedTicket);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(owner, "QA sheet could not be printed:\n" + ex.Message, "Print QA Sheet", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static Button PreviewHeaderButton(string text, string tooltip, Brush foreground, Brush background, double width = 32.0)
	{
		return new Button
		{
			Width = width,
			Height = 30.0,
			Margin = new Thickness(3.0, 9.0, 3.0, 9.0),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			ToolTip = tooltip,
			Content = new Border
			{
				Width = width - 2.0,
				Height = 28.0,
				CornerRadius = new CornerRadius(14.0),
				Background = background,
				Child = new TextBlock
				{
					Text = text,
					Foreground = foreground,
					FontSize = ((text.Length > 2) ? 11 : 16),
					FontWeight = FontWeights.Bold,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					TextAlignment = TextAlignment.Center
				}
			}
		};
	}

	private static Grid PreviewCloseGlyph(Brush foreground)
	{
		return new Grid
		{
			Width = 36.0,
			Height = 32.0,
			Children = 
			{
				(UIElement)CloseGlyphBar(foreground, 45.0),
				(UIElement)CloseGlyphBar(foreground, -45.0)
			}
		};
	}

	private static Rectangle CloseGlyphBar(Brush foreground, double angle)
	{
		return new Rectangle
		{
			Width = 13.0,
			Height = 2.0,
			RadiusX = 1.0,
			RadiusY = 1.0,
			Fill = foreground,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			RenderTransformOrigin = new Point(0.5, 0.5),
			RenderTransform = new RotateTransform(angle)
		};
	}

	private static SolidColorBrush BrushFromHex(string hex)
	{
		return new SolidColorBrush(ColorFromHex(hex));
	}

	private static Color ColorFromHex(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.Transparent));
	}
}
