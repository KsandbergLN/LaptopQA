using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace LaptopQA.Windows;

public sealed class SettingsWindow : Window
{
	private const string DefaultServiceNowRequestUrl = "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982";

	private const string DefaultServiceNowAssignmentGroupSysId = "9d144e37bdef1000e25cbf141e60d715";

	private const string DefaultServiceNowAssignmentGroupName = "Desktop Support (Miamisburg) - L2";

	private const string DefaultServiceNowTypeOfRequest = "Other";

	private const string DefaultCheckHashAndGroupTagUrl = "https://intune.microsoft.com/#view/Microsoft_Intune_Enrollment/AutopilotDevices.ReactView/filterOnManualRemediationRequired~/false";

	private const string DefaultRemoveUserFromIntuneUrl = "https://intune.microsoft.com/#view/Microsoft_Intune_DeviceSettings/DevicesWindowsMenu/~/windowsDevices";

	private const string DefaultUpdateStockroomsUrl = "https://reedelsevier.service-now.com/now/nav/ui/classic/params/target/alm_hardware_list.do%3Fsysparm_first_row%3D1%26sysparm_query%3Dserial_number%3D{SERIAL}%26sysparm_query_encoded%3Dserial_number%3D{SERIAL}%26sysparm_view%3D";

	private const string DefaultUploadHashUrl = "https://intune.microsoft.com/#view/Microsoft_Intune_Enrollment/AutopilotDevices.ReactView/filterOnManualRemediationRequired~/false";

	private readonly TextBox _name = new TextBox();

	private readonly TextBox _cameraRoll = new TextBox();

	private readonly TextBox _diagnosticsFolder = new TextBox();

	private readonly TextBox _autopilotGroupTag = new TextBox();

	private readonly TextBox _qaComputerNameFormat = new TextBox();

	private readonly TextBox _cleanupTimeout = new TextBox();

	private readonly TextBox _cleanupRetry = new TextBox();

	private readonly TextBox _wifiDelay = new TextBox();

	private readonly TextBox _ethernetRestore = new TextBox();

	private readonly TextBox _serviceNowUrl = new TextBox();

	private readonly TextBox _serviceNowType = new TextBox();

	private readonly TextBox _serviceNowAssignmentGroupName = new TextBox();

	private readonly TextBox _serviceNowAssignmentGroupSysId = new TextBox();

	private readonly TextBox _serviceNowDelay = new TextBox();

	private readonly TextBox _checkHashAndGroupTagUrl = new TextBox();

	private readonly TextBox _removeUserFromIntuneUrl = new TextBox();

	private readonly TextBox _updateStockroomsUrl = new TextBox();

	private readonly TextBox _uploadHashUrl = new TextBox();

	private readonly ComboBox _themeChoice = new ComboBox();

	private readonly ComboBox _languageChoice = new ComboBox();

	private readonly List<TextBlock> _textBlocks = new List<TextBlock>();

	private readonly List<TextBlock> _labels = new List<TextBlock>();

	private readonly List<Control> _inputs = new List<Control>();

	private readonly List<Button> _secondaryButtons = new List<Button>();

	private readonly Action<string> _previewTheme;

	private readonly string _initialTheme;

	private Border _shell = new Border();

	private Button _saveButton = new Button();

	private bool _saved;

	public AppConfig Config { get; private set; }

	public bool FactoryResetRequested { get; private set; }

	public SettingsWindow(Window owner, AppConfig config, Action<string> previewTheme)
	{
		base.Owner = owner;
		_previewTheme = previewTheme;
		_initialTheme = NormalizeTheme(config.AppTheme);
		Config = new AppConfig
		{
			TechnicianName = config.TechnicianName,
			AppTheme = config.AppTheme,
			AppLanguage = config.AppLanguage,
			CameraRoll = config.CameraRoll,
			DellDiagnosticsLogFolder = config.DellDiagnosticsLogFolder,
			CameraRollCleanupTimeoutSeconds = config.CameraRollCleanupTimeoutSeconds,
			CameraRollCleanupRetryDelaySeconds = config.CameraRollCleanupRetryDelaySeconds,
			WifiRescanEthernetDisableDelaySeconds = config.WifiRescanEthernetDisableDelaySeconds,
			EthernetRestoreDelaySeconds = config.EthernetRestoreDelaySeconds,
			DellWarrantyCliPath = config.DellWarrantyCliPath,
			AutopilotGroupTag = config.AutopilotGroupTag,
			QaComputerNameFormat = config.QaComputerNameFormat,
			ServiceNowRequestUrl = config.ServiceNowRequestUrl,
			ServiceNowTypeOfRequest = config.ServiceNowTypeOfRequest,
			ServiceNowAssignmentGroupName = config.ServiceNowAssignmentGroupName,
			ServiceNowAssignmentGroupSysId = config.ServiceNowAssignmentGroupSysId,
			ServiceNowAutomationDelayMilliseconds = config.ServiceNowAutomationDelayMilliseconds,
			CheckHashAndGroupTagUrl = config.CheckHashAndGroupTagUrl,
			RemoveUserFromIntuneUrl = config.RemoveUserFromIntuneUrl,
			UpdateStockroomsUrl = config.UpdateStockroomsUrl,
			UploadHashUrl = config.UploadHashUrl
		};
		base.Title = "Settings";
		Rect workArea = SystemParameters.WorkArea;
		base.Width = Math.Min(760.0, Math.Max(640.0, workArea.Width - 32.0));
		base.Height = Math.Min(760.0, Math.Max(680.0, workArea.Height - 32.0));
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.WindowStyle = WindowStyle.None;
		base.AllowsTransparency = true;
		base.ResizeMode = ResizeMode.NoResize;
		base.Background = Brushes.Transparent;
		base.FontFamily = new FontFamily("Segoe UI");
		Grid grid = (Grid)(base.Content = new Grid());
		_shell = new Border
		{
			Width = Math.Min(740.0, base.Width - 20.0),
			Height = base.Height - 20.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			CornerRadius = new CornerRadius(26.0),
			BorderThickness = new Thickness(1.0),
			Effect = new DropShadowEffect
			{
				BlurRadius = 28.0,
				ShadowDepth = 0.0,
				Opacity = 0.38,
				Color = ColorFromHex("#002E3A")
			}
		};
		grid.Children.Add(_shell);
		Grid grid3 = new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				},
				new RowDefinition
				{
					Height = new GridLength(50.0)
				}
			}
		};
		_shell.Child = grid3;
		Canvas canvas = new Canvas
		{
			Width = 718.0,
			Height = 690.0
		};
		ScrollViewer element = new ScrollViewer
		{
			Content = canvas,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			CanContentScroll = false,
			PanningMode = PanningMode.Both
		};
		grid3.Children.Add(element);
		Canvas canvas2 = new Canvas
		{
			Height = 50.0
		};
		Grid.SetRow(canvas2, 1);
		grid3.Children.Add(canvas2);
		_shell.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (e.OriginalSource == _shell)
			{
				try
				{
					DragMove();
				}
				catch
				{
				}
			}
		};
		TextBlock textBlock = Text("Settings", 34.0, 26.0, 300.0, 34.0, 24.0, FontWeights.Bold);
		textBlock.MouseLeftButtonDown += delegate
		{
			try
			{
				DragMove();
			}
			catch
			{
			}
		};
		canvas.Children.Add(textBlock);
		Button button = DialogButton("X", 38.0, 34.0);
		SetCanvas(button, 676.0, 26.0);
		button.Click += delegate
		{
			base.DialogResult = false;
			Close();
		};
		canvas.Children.Add(button);
		_secondaryButtons.Add(button);
		AddField(canvas, "Technician name", _name, config.TechnicianName, 36.0, 64.0, 330.0);
		AddLabel(canvas, "Language", 386.0, 64.0, 180.0);
		_languageChoice.Width = 180.0;
		_languageChoice.Height = 26.0;
		foreach (AppLanguage item in LanguageCatalog.All)
		{
			_languageChoice.Items.Add(item);
		}
		_languageChoice.SelectedItem = LanguageCatalog.Resolve(config.AppLanguage);
		SetCanvas(_languageChoice, 386.0, 82.0);
		canvas.Children.Add(_languageChoice);
		_inputs.Add(_languageChoice);
		AddLabel(canvas, "Theme", 582.0, 64.0, 118.0);
		_themeChoice.Width = 118.0;
		_themeChoice.Height = 26.0;
		_themeChoice.Items.Add("Light");
		_themeChoice.Items.Add("Dark");
		_themeChoice.Items.Add("AMOLED");
		_themeChoice.SelectedItem = _initialTheme;
		SetCanvas(_themeChoice, 582.0, 82.0);
		canvas.Children.Add(_themeChoice);
		_inputs.Add(_themeChoice);
		AddField(canvas, "Autopilot group tag", _autopilotGroupTag, string.IsNullOrWhiteSpace(config.AutopilotGroupTag) ? "LNG AAD" : config.AutopilotGroupTag, 36.0, 110.0, 330.0);
		AddField(canvas, "Device name format", _qaComputerNameFormat, string.IsNullOrWhiteSpace(config.QaComputerNameFormat) ? "LNG-{serial}" : config.QaComputerNameFormat, 386.0, 110.0, 314.0);
		_qaComputerNameFormat.ToolTip = "Controls the device name used in the app, on QA sheets, and in saved files. Available values: {serial}, {computer}, and {asset}.";
		AddBrowseField(canvas, "Camera Roll folder", _cameraRoll, config.CameraRoll, 36.0, 156.0, 568.0);
		AddBrowseField(canvas, "Dell diagnostics log folder (optional)", _diagnosticsFolder, config.DellDiagnosticsLogFolder, 36.0, 202.0, 568.0);
		AddField(canvas, "Camera Roll cleanup timeout seconds", _cleanupTimeout, config.CameraRollCleanupTimeoutSeconds.ToString(CultureInfo.InvariantCulture), 36.0, 248.0, 190.0);
		AddField(canvas, "Cleanup retry delay seconds", _cleanupRetry, config.CameraRollCleanupRetryDelaySeconds.ToString(CultureInfo.InvariantCulture), 386.0, 248.0, 190.0);
		AddField(canvas, "Wi-Fi rescan wait seconds", _wifiDelay, config.WifiRescanEthernetDisableDelaySeconds.ToString(CultureInfo.InvariantCulture), 36.0, 294.0, 190.0);
		AddField(canvas, "Ethernet restore wait seconds", _ethernetRestore, config.EthernetRestoreDelaySeconds.ToString(CultureInfo.InvariantCulture), 386.0, 294.0, 190.0);
		AddField(canvas, "ServiceNow request URL", _serviceNowUrl, string.IsNullOrWhiteSpace(config.ServiceNowRequestUrl) ? "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982" : config.ServiceNowRequestUrl, 36.0, 340.0, 664.0);
		AddField(canvas, "ServiceNow type of request", _serviceNowType, string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest, 36.0, 386.0, 330.0);
		AddField(canvas, "ServiceNow automation wait milliseconds", _serviceNowDelay, ((config.ServiceNowAutomationDelayMilliseconds <= 0) ? 500 : config.ServiceNowAutomationDelayMilliseconds).ToString(CultureInfo.InvariantCulture), 386.0, 386.0, 314.0);
		AddField(canvas, "ServiceNow assignment group name", _serviceNowAssignmentGroupName, string.IsNullOrWhiteSpace(config.ServiceNowAssignmentGroupName) ? "Desktop Support (Miamisburg) - L2" : config.ServiceNowAssignmentGroupName, 36.0, 432.0, 330.0);
		AddField(canvas, "ServiceNow assignment group sys ID", _serviceNowAssignmentGroupSysId, string.IsNullOrWhiteSpace(config.ServiceNowAssignmentGroupSysId) ? "9d144e37bdef1000e25cbf141e60d715" : config.ServiceNowAssignmentGroupSysId, 386.0, 432.0, 314.0);
		TextBlock finalCheckLinksTitle = Text("Final Check Links", 36.0, 480.0, 250.0, 24.0, 15.0, FontWeights.Bold);
		canvas.Children.Add(finalCheckLinksTitle);
		AddField(canvas, "Check Hash and Group Tag URL", _checkHashAndGroupTagUrl, string.IsNullOrWhiteSpace(config.CheckHashAndGroupTagUrl) ? DefaultCheckHashAndGroupTagUrl : config.CheckHashAndGroupTagUrl, 36.0, 506.0, 664.0);
		AddField(canvas, "Remove User from Laptop in Intune URL", _removeUserFromIntuneUrl, string.IsNullOrWhiteSpace(config.RemoveUserFromIntuneUrl) ? DefaultRemoveUserFromIntuneUrl : config.RemoveUserFromIntuneUrl, 36.0, 552.0, 664.0);
		AddField(canvas, "Update Stockrooms URL", _updateStockroomsUrl, string.IsNullOrWhiteSpace(config.UpdateStockroomsUrl) ? DefaultUpdateStockroomsUrl : config.UpdateStockroomsUrl, 36.0, 598.0, 664.0);
		_updateStockroomsUrl.ToolTip = "Use {SERIAL} where the current laptop's service tag should be placed in the ServiceNow URL.";
		AddField(canvas, "Upload Hash URL", _uploadHashUrl, string.IsNullOrWhiteSpace(config.UploadHashUrl) ? DefaultUploadHashUrl : config.UploadHashUrl, 36.0, 644.0, 664.0);
		_saveButton = DialogButton("Save", 80.0, 32.0);
		SetCanvas(_saveButton, 538.0, 9.0);
		_saveButton.Click += delegate
		{
			Save();
		};
		canvas2.Children.Add(_saveButton);
		Button button2 = DialogButton("Reset Settings", 126.0, 32.0);
		button2.ToolTip = "Reset all configuration defaults and remove the saved technician name. Saved QA sheets and logs are kept.";
		SetCanvas(button2, 36.0, 9.0);
		button2.Click += delegate
		{
			ResetFactorySettings();
		};
		canvas2.Children.Add(button2);
		_secondaryButtons.Add(button2);
		Button button3 = DialogButton("Cancel", 80.0, 32.0);
		SetCanvas(button3, 628.0, 9.0);
		button3.Click += delegate
		{
			base.DialogResult = false;
			Close();
		};
		canvas2.Children.Add(button3);
		_secondaryButtons.Add(button3);
		_themeChoice.SelectionChanged += delegate
		{
			PreviewTheme(_themeChoice.SelectedItem?.ToString() ?? "Dark");
		};
		_languageChoice.SelectionChanged += delegate
		{
			Config.AppLanguage = (_languageChoice.SelectedItem as AppLanguage)?.Code ?? "en-US";
			LanguageCatalog.ApplyCulture(Config.AppLanguage);
			WpfLocalization.Apply(this, Config.AppLanguage);
		};
		base.Closing += delegate
		{
			if (!_saved)
			{
				_previewTheme(_initialTheme);
			}
		};
		ApplyDialogTheme(_initialTheme);
		WpfLocalization.Apply(this, Config.AppLanguage);
	}

	private void AddField(Canvas canvas, string label, TextBox box, string value, double left, double top, double width)
	{
		AddLabel(canvas, label, left, top, Math.Max(width, 250.0));
		box.Text = value;
		box.Width = width;
		box.Height = 26.0;
		SetCanvas(box, left, top + 18.0);
		canvas.Children.Add(box);
		_inputs.Add(box);
	}

	private void AddBrowseField(Canvas canvas, string label, TextBox box, string value, double left, double top, double width)
	{
		AddField(canvas, label, box, value, left, top, width);
		Button button = DialogButton("Browse", 88.0, 26.0);
		SetCanvas(button, left + width + 8.0, top + 18.0);
		button.Click += delegate
		{
			BrowseForFolder(label, box);
		};
		canvas.Children.Add(button);
		_secondaryButtons.Add(button);
	}

	private void AddBrowseFileField(Canvas canvas, string label, TextBox box, string value, double left, double top, double width)
	{
		AddField(canvas, label, box, value, left, top, width);
		Button button = DialogButton("Browse", 88.0, 26.0);
		SetCanvas(button, left + width + 8.0, top + 18.0);
		button.Click += delegate
		{
			BrowseForFile(label, box);
		};
		canvas.Children.Add(button);
		_secondaryButtons.Add(button);
	}

	private void AddPassword(Canvas canvas, string label, PasswordBox box, double left, double top, double width)
	{
		AddLabel(canvas, label, left, top, Math.Max(width, 250.0));
		box.Width = width;
		box.Height = 30.0;
		SetCanvas(box, left, top + 22.0);
		canvas.Children.Add(box);
		_inputs.Add(box);
	}

	private void AddLabel(Canvas canvas, string text, double left, double top, double width)
	{
		TextBlock textBlock = Text(text, left, top, width, 17.0, 11.5, FontWeights.Normal);
		canvas.Children.Add(textBlock);
		_labels.Add(textBlock);
	}

	private void BrowseForFolder(string title, TextBox target)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = title,
			InitialDirectory = (Directory.Exists(target.Text.Trim()) ? target.Text.Trim() : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			target.Text = openFolderDialog.FolderName;
		}
	}

	private void BrowseForFile(string title, TextBox target)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = title,
			Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
			InitialDirectory = (File.Exists(target.Text.Trim()) ? Path.GetDirectoryName(target.Text.Trim()) : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
		};
		if (openFileDialog.ShowDialog(this) == true)
		{
			target.Text = openFileDialog.FileName;
		}
	}

	private void Save()
	{
		Config.TechnicianName = _name.Text.Trim();
		Config.AppTheme = NormalizeTheme(_themeChoice.SelectedItem?.ToString());
		Config.AppLanguage = (_languageChoice.SelectedItem as AppLanguage)?.Code ?? "en-US";
		Config.AutopilotGroupTag = string.IsNullOrWhiteSpace(_autopilotGroupTag.Text) ? "LNG AAD" : _autopilotGroupTag.Text.Trim();
		Config.QaComputerNameFormat = string.IsNullOrWhiteSpace(_qaComputerNameFormat.Text) ? "LNG-{serial}" : _qaComputerNameFormat.Text.Trim();
		Config.CameraRoll = _cameraRoll.Text.Trim();
		Config.DellDiagnosticsLogFolder = _diagnosticsFolder.Text.Trim();
		Config.ServiceNowRequestUrl = (string.IsNullOrWhiteSpace(_serviceNowUrl.Text) ? "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982" : _serviceNowUrl.Text.Trim());
		Config.ServiceNowTypeOfRequest = (string.IsNullOrWhiteSpace(_serviceNowType.Text) ? "Other" : _serviceNowType.Text.Trim());
		Config.ServiceNowAssignmentGroupName = (string.IsNullOrWhiteSpace(_serviceNowAssignmentGroupName.Text) ? "Desktop Support (Miamisburg) - L2" : _serviceNowAssignmentGroupName.Text.Trim());
		Config.ServiceNowAssignmentGroupSysId = (string.IsNullOrWhiteSpace(_serviceNowAssignmentGroupSysId.Text) ? "9d144e37bdef1000e25cbf141e60d715" : _serviceNowAssignmentGroupSysId.Text.Trim());
		Config.CheckHashAndGroupTagUrl = string.IsNullOrWhiteSpace(_checkHashAndGroupTagUrl.Text) ? DefaultCheckHashAndGroupTagUrl : _checkHashAndGroupTagUrl.Text.Trim();
		Config.RemoveUserFromIntuneUrl = string.IsNullOrWhiteSpace(_removeUserFromIntuneUrl.Text) ? DefaultRemoveUserFromIntuneUrl : _removeUserFromIntuneUrl.Text.Trim();
		Config.UpdateStockroomsUrl = string.IsNullOrWhiteSpace(_updateStockroomsUrl.Text) ? DefaultUpdateStockroomsUrl : _updateStockroomsUrl.Text.Trim();
		Config.UploadHashUrl = string.IsNullOrWhiteSpace(_uploadHashUrl.Text) ? DefaultUploadHashUrl : _uploadHashUrl.Text.Trim();
		if (TryReadPositiveInt(_cleanupTimeout, "Camera Roll cleanup timeout seconds", out var value) && TryReadPositiveInt(_cleanupRetry, "Cleanup retry delay seconds", out var value2) && TryReadPositiveInt(_wifiDelay, "Wi-Fi rescan wait seconds", out var value3) && TryReadPositiveInt(_ethernetRestore, "Ethernet restore wait seconds", out var value4) && TryReadPositiveInt(_serviceNowDelay, "ServiceNow automation wait milliseconds", out var value5))
		{
			Config.CameraRollCleanupTimeoutSeconds = value;
			Config.CameraRollCleanupRetryDelaySeconds = value2;
			Config.WifiRescanEthernetDisableDelaySeconds = value3;
			Config.EthernetRestoreDelaySeconds = value4;
			Config.ServiceNowAutomationDelayMilliseconds = Math.Clamp(value5, 500, 30000);
			_saved = true;
			base.DialogResult = true;
			Close();
		}
	}

	private void ResetFactorySettings()
	{
		if (MessageBox.Show(this, "Reset every configuration setting to its default and remove the saved technician name?\n\nSaved QA sheets, logs, and hardware files will not be deleted.", "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			Config = new AppConfig();
			FactoryResetRequested = true;
			_saved = true;
			_previewTheme(Config.AppTheme);
			base.DialogResult = true;
			Close();
		}
	}

	private bool TryReadPositiveInt(TextBox box, string label, out int value)
	{
		if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
		{
			return true;
		}
		MessageBox.Show(this, label + " must be a whole number.", "Settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		box.Focus();
		return false;
	}

	private void PreviewTheme(string theme)
	{
		Config.AppTheme = NormalizeTheme(theme);
		ApplyDialogTheme(Config.AppTheme);
		_previewTheme(Config.AppTheme);
	}

	private void ApplyDialogTheme(string theme)
	{
		string a = NormalizeTheme(theme);
		bool flag = string.Equals(a, "Light", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(a, "AMOLED", StringComparison.OrdinalIgnoreCase);
		string hex = (flag ? "#EFFAFAF6" : (flag2 ? "#F0000000" : "#E0374F59"));
		string hex2 = (flag ? "#EEF0F1EC" : (flag2 ? "#F0080808" : "#E0162D38"));
		_shell.Background = new LinearGradientBrush(ColorFromHex(hex), ColorFromHex(hex2), new Point(0.0, 0.0), new Point(1.0, 1.0));
		_shell.BorderBrush = BrushFromHex(flag ? "#9BAFB5" : (flag2 ? "#5A5A5A" : "#77D6F6FF"));
		if (_shell.Effect is DropShadowEffect dropShadowEffect)
		{
			dropShadowEffect.Color = ColorFromHex(flag ? "#657A80" : (flag2 ? "#000000" : "#002E3A"));
			dropShadowEffect.Opacity = (flag ? 0.3 : (flag2 ? 0.54 : 0.38));
		}
		Brush brush = BrushFromHex(flag ? "#06141B" : (flag2 ? "#F4F4F4" : "#F8FAFC"));
		Brush brush2 = BrushFromHex(flag ? "#1D323C" : (flag2 ? "#BDBDBD" : "#C9E2E8"));
		Brush background = BrushFromHex(flag ? "#FFFAFAF6" : (flag2 ? "#FF080808" : "#24414B"));
		Brush borderBrush = BrushFromHex(flag ? "#78909A" : (flag2 ? "#666666" : "#6682949B"));
		base.Foreground = brush;
		foreach (TextBlock label in _labels)
		{
			label.Foreground = brush2;
		}
		foreach (Control input in _inputs)
		{
			input.Background = background;
			input.Foreground = brush;
			input.BorderBrush = borderBrush;
			if (input is TextBox textBox)
			{
				textBox.CaretBrush = brush;
				textBox.SelectionBrush = BrushFromHex(flag ? "#2F6F68" : (flag2 ? "#666666" : "#A2E6DD"));
			}
			else if (input is PasswordBox passwordBox)
			{
				passwordBox.CaretBrush = brush;
				passwordBox.SelectionBrush = BrushFromHex(flag ? "#2F6F68" : (flag2 ? "#666666" : "#A2E6DD"));
			}
		}
		ApplyComboBoxTheme(_languageChoice, flag, flag2, borderBrush);
		ApplyComboBoxTheme(_themeChoice, flag, flag2, borderBrush);
		foreach (TextBlock item in _textBlocks.Where((TextBlock b) => !_labels.Contains(b)))
		{
			item.Foreground = ((item.FontSize >= 18.0) ? brush : brush2);
		}
		foreach (Button secondaryButton in _secondaryButtons)
		{
			secondaryButton.Background = BrushFromHex(flag ? "#D8E1DF" : (flag2 ? "#303030" : "#485D66"));
			secondaryButton.Foreground = brush;
		}
		_saveButton.Background = BrushFromHex(flag2 ? "#E0E0E0" : "#A2E6DD");
		_saveButton.Foreground = BrushFromHex(flag2 ? "#050505" : "#073F55");
	}

	private static void ApplyComboBoxTheme(ComboBox comboBox, bool isLight, bool isAmoled, Brush borderBrush)
	{
		Brush comboBackground = BrushFromHex(isLight ? "#FFFAFAF6" : (isAmoled ? "#FFE8E8E8" : "#FFE0E0E0"));
		Brush comboForeground = BrushFromHex("#050505");
		Brush selectedBackground = BrushFromHex(isLight ? "#D8E1DF" : (isAmoled ? "#FFCFCFCF" : "#FFC9D4D8"));

		comboBox.Background = comboBackground;
		comboBox.Foreground = comboForeground;
		comboBox.BorderBrush = borderBrush;

		Style itemStyle = new Style(typeof(ComboBoxItem));
		itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, comboBackground));
		itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, comboForeground));
		itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8.0, 4.0, 8.0, 4.0)));
		itemStyle.Triggers.Add(new Trigger
		{
			Property = ComboBoxItem.IsHighlightedProperty,
			Value = true,
			Setters =
			{
				new Setter(Control.BackgroundProperty, selectedBackground),
				new Setter(Control.ForegroundProperty, comboForeground)
			}
		});
		itemStyle.Triggers.Add(new Trigger
		{
			Property = ComboBoxItem.IsSelectedProperty,
			Value = true,
			Setters =
			{
				new Setter(Control.BackgroundProperty, selectedBackground),
				new Setter(Control.ForegroundProperty, comboForeground)
			}
		});
		comboBox.ItemContainerStyle = itemStyle;
	}

	private TextBlock Text(string text, double left, double top, double width, double height, double fontSize, FontWeight weight)
	{
		TextBlock textBlock = new TextBlock
		{
			Text = text,
			Width = width,
			Height = height,
			FontSize = fontSize,
			FontWeight = weight,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		SetCanvas(textBlock, left, top);
		_textBlocks.Add(textBlock);
		return textBlock;
	}

	private static Button DialogButton(string text, double width, double height)
	{
		return new Button
		{
			Content = text,
			Width = width,
			Height = height,
			FontWeight = FontWeights.Bold,
			BorderThickness = new Thickness(0.0),
			Cursor = Cursors.Hand,
			Template = ButtonChrome.RoundedTemplate()
		};
	}

	private static ControlTemplate ToggleTemplate()
	{
		return (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"{x:Type ToggleButton}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n    <Grid Width=\"52\" Height=\"26\">\n        <Border x:Name=\"Track\" CornerRadius=\"13\" Background=\"#485D66\" BorderBrush=\"#6682949B\" BorderThickness=\"1\"/>\n        <Ellipse x:Name=\"Knob\" Width=\"20\" Height=\"20\" Margin=\"3,0,29,0\" Fill=\"#F8FAFC\"/>\n    </Grid>\n    <ControlTemplate.Triggers>\n        <Trigger Property=\"IsMouseOver\" Value=\"True\">\n            <Setter TargetName=\"Track\" Property=\"Opacity\" Value=\"0.86\"/>\n        </Trigger>\n        <Trigger Property=\"IsChecked\" Value=\"True\">\n            <Setter TargetName=\"Track\" Property=\"Background\" Value=\"#A2E6DD\"/>\n            <Setter TargetName=\"Track\" Property=\"BorderBrush\" Value=\"#A2E6DD\"/>\n            <Setter TargetName=\"Knob\" Property=\"Fill\" Value=\"#073F55\"/>\n            <Setter TargetName=\"Knob\" Property=\"Margin\" Value=\"29,0,3,0\"/>\n        </Trigger>\n    </ControlTemplate.Triggers>\n</ControlTemplate>");
	}

	private static void SetCanvas(FrameworkElement element, double left, double top)
	{
		Canvas.SetLeft(element, left);
		Canvas.SetTop(element, top);
	}

	private static string NormalizeTheme(string? theme)
	{
		if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
		{
			return "Light";
		}
		if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
		{
			return "Dark";
		}
		if (string.Equals(theme, "AMOLED", StringComparison.OrdinalIgnoreCase) || string.Equals(theme, "Amoled", StringComparison.OrdinalIgnoreCase))
		{
			return "AMOLED";
		}
		return "Light";
	}

	private static Brush BrushFromHex(string hex)
	{
		return new SolidColorBrush(ColorFromHex(hex));
	}

	private static Color ColorFromHex(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.Transparent));
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is T val)
			{
				yield return val;
			}
			foreach (T item in FindVisualChildren<T>(child))
			{
				yield return item;
			}
		}
	}
}
