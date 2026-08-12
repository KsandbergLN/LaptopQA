using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaptopQA.Shared;
using Microsoft.Win32;

namespace LaptopQA.Windows;

public partial class MainWindow : Window, IComponentConnector
{
	#region Shared types, constants, and runtime state

	private sealed record DataRootCandidate(string Path, bool HasMarker, DateTime SessionWriteUtc);

	private sealed record ThemePalette(string Name, string Text, string Muted, string Accent, string PanelStroke, string InputStroke, string PrimaryButton, string ResetButton, string DangerButton, IReadOnlyList<string> Shell, IReadOnlyList<string> GlassPanel, IReadOnlyList<string> DarkGlass, IReadOnlyList<string> UtilityPanel, string ActivityPanel, string SplashOverlay, string NoteInput, string WaveBack, string WaveFront, string FinalCheckChecked, string FinalCheckCheckedBox, string FinalCheckCheckMark, string DrawerForeground, string ShellShadow)
	{
		public static ThemePalette For(string name)
		{
			if (name == "Light")
			{
				return new ThemePalette("Light", "#06141B", "#1D323C", "#004F4A", "#7F969F", "#78909A", "#60757E", "#203741", "#9B3036", new string[3] { "#FAFAF6", "#F0F1EC", "#E3E6E0" }, new string[3] { "#FFFFFFFF", "#FFF9FAF7", "#FFF1F4F0" }, new string[2] { "#FFFFFFFF", "#FFF7F8F4" }, new string[3] { "#FFFFFFFF", "#FFF9FAF7", "#FFF1F4F0" }, "#FFFFFFFF", "#EEF0F1EC", "#FFFFFFFF", "#30B5C0C5", "#28A4AFB8", "#DCEBE8", "#C8DBD7", "#12633D", "#FFFFFF", "#657A80");
			}
			if (name == "AMOLED")
			{
				return new ThemePalette("AMOLED", "#F4F4F4", "#BDBDBD", "#D8D8D8", "#5A5A5A", "#666666", "#343434", "#1A1A1A", "#4A4A4A", new string[3] { "#000000", "#000000", "#090909" }, new string[3] { "#F0101010", "#E00A0A0A", "#D0050505" }, new string[2] { "#F0080808", "#E0000000" }, new string[3] { "#F00E0E0E", "#E0080808", "#D0000000" }, "#FF050505", "#F0000000", "#FF070707", "#10000000", "#18000000", "#222222", "#DADADA", "#050505", "#F4F4F4", "#000000");
			}
			return new ThemePalette("Dark", "#F3F7F8", "#B9C7CB", "#A2E6DD", "#6682949B", "#3FAED5DF", "#60757E", "#263D46", "#8A4646", new string[3] { "#253640", "#314A55", "#526A70" }, new string[3] { "#5B536B72", "#463F5962", "#3831444D" }, new string[2] { "#A01D3038", "#7F142730" }, new string[3] { "#8A142933", "#74213842", "#662D4D55" }, "#FF2A414A", "#F0253640", "#A01D3038", "#2B17495C", "#3510394A", "#245C5C", "#A2E6DD", "#102A2D", "#FFFFFF", "#000000");
		}
	}

	private sealed class StartupJokeDeckState
	{
		public int JokeCount { get; set; }

		public List<int> Order { get; set; } = new List<int>();

		public int Position { get; set; }

		public int LastIndex { get; set; } = -1;
	}

	private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

	private struct SystemPowerStatus
	{
		public byte ACLineStatus;

		public byte BatteryFlag;

		public byte BatteryLifePercent;

		public byte SystemStatusFlag;

		public uint BatteryLifeTime;

		public uint BatteryFullLifeTime;
	}

	private sealed record QaRenderRow(string Number, string Task, string State, string Detail);

	private sealed record CachedSessionOption(string FilePath, QaSessionCache Session, string DisplayName)
	{
		public override string ToString() => DisplayName;
	}

	private sealed class CachedSessionIndexEntry
	{
		public string SessionId { get; set; } = "";

		public string FileName { get; set; } = "";

		public string ServiceTag { get; set; } = "";

		public DateTime StartedAt { get; set; }

		public DateTime SavedAt { get; set; }
	}

	private sealed record UsbPortObservation(string Name, string Path);

	private const string DataDriveMarkerFileName = "Laptop-QA-Drive.json";

	private readonly string _appRoot = AppContext.BaseDirectory;

	private readonly string _dataRoot = ResolveDataRoot();

	private readonly bool _removableDataDriveDetected = FindPreferredRemovableDataRoot() != null;

	private readonly Dictionary<string, string> _states = new Dictionary<string, string>
	{
		["SecureBoot"] = "Unknown",
		["PrimaryAC"] = "Unknown",
		["WiFi"] = "Waiting",
		["Ethernet"] = "Waiting",
		["Camera"] = "Waiting",
		["ExternalVideo"] = "Waiting",
		["Keyboard"] = "Waiting",
		["Diagnostics"] = "Warning",
		["UsbPorts"] = "Waiting"
	};

	private readonly Dictionary<string, string> _details = new Dictionary<string, string>();

	private readonly List<string> _activity = new List<string>();

	private readonly HashSet<string> _processingOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private AppConfig _config = new AppConfig();

	private HardwareSnapshot _hardware = new HardwareSnapshot();

	private string _serviceTag = "";

	private string _assetTag = "";

	private string _warranty = "Unknown";

	private string _warrantyCachedServiceTag = "";

	private DateTime _warrantyComparisonDate = DateTime.Today;

	private string _warrantyComparisonDateSource = "Windows system clock";

	private string _batterySummary = "Battery Health: unavailable";

	private string _batteryHealthRating = "";

	private CurrentBatterySnapshot _currentBattery = new CurrentBatterySnapshot();

	private string _diagnosticsLogPath = "";

	private string _diagnosticsRawText = "";

	private int _cameraTestRunId;

	private Task<string>? _cameraCleanupTask;

	private const string DefaultServiceNowRequestUrl = "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982";

	private const string DefaultServiceNowAssignmentGroupSysId = "9d144e37bdef1000e25cbf141e60d715";

	private const string DefaultServiceNowAssignmentGroupName = "Desktop Support (Miamisburg) - L2";

	private const string DefaultServiceNowTypeOfRequest = "Other";

	private const string DefaultWaveBackData = "M -20,440 C 190,370 380,550 580,470 C 760,400 980,555 1300,465 L1300,740 L-20,740 Z";

	private const string DefaultWaveFrontData = "M -20,500 C 230,430 420,615 680,525 C 880,455 1080,600 1300,515 L1300,740 L-20,740 Z";

	private const int QaAndDiagnosticsRetentionDays = 90;

	private bool _notesOpen;

	private bool _activityOpen;

	private bool _hardwareOpen;

	private bool _foldersOpen;

	private readonly List<string> _drawerOrder = new List<string>();

	private string _currentTheme = "Light";

	private bool _suppressQaSessionCache;

	private readonly DispatcherTimer _qaSessionSaveTimer;

	private bool _startupDataRefreshRequired;

	private bool _warrantyWaitingForNetwork;

	private bool _closeCleanupComplete;

	private DispatcherTimer? _externalDisplayPollTimer;

	private bool _externalDisplayScanRunning;

	private DispatcherTimer? _currentBatteryPollTimer;

	private bool _currentBatteryRefreshRunning;

	private DispatcherTimer? _usbPortPollTimer;

	private DispatcherTimer? _usbDeviceChangeDebounceTimer;

	private HwndSource? _windowSource;

	private bool _usbPortScanRunning;

	private bool _usbPortTestActive;

	private bool _usbPortTestFinished;

	private bool _qaLiveMonitoringActive;

	private readonly List<UsbPortCache> _usbPorts = new List<UsbPortCache>();

	private readonly List<UsbPortObservation> _usbDockObservations = new List<UsbPortObservation>();

	private readonly HashSet<string> _usbPreviousPresentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private string _usbPortDetectionAdjustment = "";

	private bool _qaSessionReady;

	private bool _completionCelebrated;

	private bool _removableDriveWarningShown;

	private DateTime _qaSessionCacheWriteUtc;

	private string _activeQaSessionId = "";

	private DateTime _activeQaSessionStartedAt;

	private readonly List<CachedSessionOption> _cachedSessionOptions = new List<CachedSessionOption>();

	private bool _updatingCachedSessionPicker;

	private DispatcherTimer? _headerClockTimer;

	private DateTime _configWriteUtc;

	private static readonly string[] StartupLoadingJokes = new string[365]
	{
		"Asking Windows where it put the important stuff.", "Reticulating spreadsheets. It sounds official.", "Checking if the laptop woke up on the productive side.", "Polishing the pixels before they clock in.", "Convincing BIOS settings to answer politely.", "Counting cores without making them feel judged.", "Looking for warranty data in the usual hiding places.", "Teaching the progress bar the value of suspense.", "Checking battery health and pretending not to stare.", "Giving the network adapters a quick pep talk.",
		"Loading the serious tools with unserious confidence.", "Negotiating with PowerShell for a clean answer.", "Asking the service tag to please use its indoor voice.", "Warming up the QA clipboard.", "Measuring laptop vibes with enterprise precision.", "Making sure the hash folder remembers where it lives.", "Consulting the ancient scrolls of device inventory.", "Opening a polite support ticket with the startup routine.", "Letting the firmware finish its morning coffee.", "Auditing buttons for excessive buttonness.",
		"Preparing checkboxes for their moment of glory.", "Double-checking that double-checking is checked.", "Loading notes, specs, and a healthy sense of doubt.", "Putting the QA sheet into business casual.", "Making the activity log look busy because it is.", "Persuading adapters to identify themselves.", "Checking the keyboard tester's dramatic entrance.", "Finding the asset tag without making eye contact.", "Running a tiny meeting with the hardware snapshot.", "Waiting for Dell data to return from its side quest.",
		"Folding cables emotionally, not physically.", "Syncing vibes with the current theme.", "Assembling buttons into a respectable row.", "Teaching the splash screen some new material.", "Reading system details at a socially acceptable speed.", "Making sure Ready means ready, not mostly ready.", "Checking if Secure Boot is feeling secure.", "Looking for AC settings with a flashlight and optimism.", "Loading the part where everything looks intentional.", "Making the interface less like a spreadsheet wearing a coat.",
		"Asking the laptop if it has any final concerns.", "Preparing the pass/fail buttons for judgment day.", "Checking the camera path without opening any weird folders.", "Making sure the report generator remembered its tie.", "Turning random system facts into useful QA notes.", "Reserving a table for BIOS, battery, and warranty.", "Gathering laptop facts into a neat little pile.", "Consulting the registry, but only because we have to.", "Waiting for Windows Management Instrumentation to manage itself.", "Giving the loading screen room to be charming.",
		"Doing the invisible work that makes the visible work nicer.", "Checking hardware while the progress bar practices patience.", "Making sure the close button stays out of trouble.", "Organizing startup tasks into a tiny parade.", "Letting the app stretch before the real work starts.", "Turning device chaos into something printable.", "Scanning for useful facts and suspicious silence.", "Preparing a clean QA runway.", "Calibrating the button confidence meter.", "Making startup look calmer than it feels.",
		"Checking whether the charger is plugged in or just emotionally present.", "Asking the fan to keep it down during the inventory meeting.", "Separating facts from things the laptop confidently guessed.", "Giving the service tag one last chance to be legible.", "Making sure the report has all its ducks in a row.", "Waiting for one more spinner to finish spinning.", "Telling the USB drive this is a no-drama zone.", "Checking pixels for attendance.", "Running a background check on the background processes.", "Aligning the QA stars.",
		"Inviting the cache to share its memories.", "Making the diagnostics log tell the whole story.", "Counting ports so nobody feels left out.", "Asking the display to stay on topic.", "Putting the laptop through a very small audit.", "Negotiating a ceasefire between sleep mode and uptime.", "Making sure every green check earned its color.", "Turning a pile of checks into a finished laptop.", "Checking if the fan has opinions about this test.", "Loading confidence, one adapter at a time.",
		"Asking Windows to hold still while we count everything.", "Waiting for Windows to finish explaining itself.", "Checking whether rebooting is a suggestion or a lifestyle.", "Giving Task Manager a moment to look presentable.", "Loading Windows at the speed of corporate approval.", "Looking for the setting Windows moved since yesterday.", "Asking the registry to keep this conversation confidential.", "Checking whether the desktop is desktoping.", "Waiting for a background process to finish its foreground speech.", "Politely declining another Windows tip.",
		"Making sure the Start menu knows where it started.", "Checking whether uptime has become a personality trait.", "Giving Windows Update a fake calendar invitation.", "Counting services and pretending every one has a purpose.", "Letting Windows gather its thoughts and several gigabytes.", "Interviewing the battery about its long-term goals.", "Checking whether eighty percent is a number or a negotiation.", "Asking the charger to define connected.", "Measuring battery optimism in watt-hours.", "Giving the battery icon a more accurate poker face.",
		"Checking whether the laptop is charging or merely hopeful.", "Asking AC power to bring a valid ID.", "Reviewing the battery's performance without using the word annual.", "Waiting for the charge state to stop changing its story.", "Counting battery cycles like rings on a very electronic tree.", "Checking whether the power plan has an actual plan.", "Asking the battery to please remain positive.", "Calculating how long five percent thinks it can last.", "Making sure the charger and laptop are still on speaking terms.", "Checking battery health with excellent bedside manner.",
		"Asking Wi-Fi which network it thinks it joined.", "Giving Ethernet the chance to be the reliable sibling.", "Checking whether the IP address has a forwarding address.", "Waiting for DNS to remember everybody's name.", "Asking the network adapter to adapt.", "Testing connectivity without making eye contact with the router.", "Following the packets to see where they spend their time.", "Checking if the gateway is open for business.", "Giving Wi-Fi three bars and a pep talk.", "Asking Ethernet why it brought two unidentified networks.",
		"Making sure localhost has not wandered off.", "Checking whether the subnet mask fits.", "Waiting for DHCP to finish handing out numbers.", "Asking the firewall to be firm but fair.", "Counting network hops without spilling the packets.", "Asking the monitor to make an appearance.", "Checking whether HDMI arrived fashionably late.", "Giving the second display a supporting role.", "Counting pixels before they scatter.", "Asking Display Settings to tell the whole resolution.",
		"Checking if portrait mode is standing correctly.", "Waiting for the monitor handshake to become less awkward.", "Making sure duplicate and extend are not synonyms today.", "Asking the graphics adapter to draw a conclusion.", "Checking whether the external display is externally available.", "Giving every monitor a fair chance to be primary.", "Inspecting refresh rates for signs of exhaustion.", "Asking the cable to stop sending mixed signals.", "Checking if the screen is black or simply in dark mode.", "Making room on the desktop for one more desktop.",
		"Asking the camera to look alive.", "Checking whether the privacy shutter is committed to privacy.", "Giving the webcam time to find its good angle.", "Testing the microphone without saying testing one two.", "Asking Camera Roll to tidy up before company arrives.", "Checking if mute is a setting or a personal boundary.", "Letting the speakers clear their throats.", "Asking the webcam light to keep spoilers to itself.", "Checking whether the camera can focus on the task.", "Giving audio defaults a chance to become actual defaults.",
		"Testing the lens without making it self-conscious.", "Asking the microphone to use its indoor gain.", "Checking whether the speakers have anything constructive to add.", "Making sure the camera is present and accounted for.", "Waiting for the audio driver to drop the beat responsibly.", "Giving every key a chance to speak.", "Checking whether Caps Lock is feeling important.", "Asking the space bar to respect personal space.", "Testing Escape while carefully avoiding an escape.", "Making sure Backspace has no unfinished business.",
		"Checking whether the function keys know their function.", "Giving the trackpad a smooth performance review.", "Asking Num Lock to pick a side.", "Checking if the arrow keys know where this is going.", "Letting Enter make an entrance.", "Testing Shift during regular business hours.", "Asking Ctrl to remain in control.", "Checking whether Alt has an alternative plan.", "Giving the keyboard a type-cast role.", "Making sure Delete is used responsibly.",
		"Reading the diagnostic log so nobody else has to.", "Asking the error code to be more specific.", "Checking whether the warning is worried or just cautious.", "Giving the log file a chance to vent.", "Looking for failures hiding behind successful wording.", "Asking diagnostics to show its work.", "Checking timestamps for signs of time travel.", "Turning raw logs into lightly cooked information.", "Asking the test result to answer yes or no eventually.", "Checking whether no issues found found any issues.",
		"Giving every prompt response the benefit of the doubt.", "Reading between the log lines with enterprise-grade glasses.", "Asking the old diagnostic file if it remembers this laptop.", "Checking whether the failure is technical or theatrical.", "Making the diagnostics summary less diagnostically mysterious.", "Knocking before entering BIOS.", "Asking Secure Boot if it feels secure today.", "Checking firmware for fresh opinions.", "Giving BIOS settings a respectful amount of distance.", "Asking UEFI to spell its name slowly.",
		"Checking whether factory defaults remember the factory.", "Waiting for firmware to finish being firm.", "Asking the boot order to form a single line.", "Checking if legacy mode is feeling nostalgic.", "Giving Secure Boot a tiny security blanket.", "Asking TPM to keep the secrets secret.", "Checking whether the firmware password knows the password.", "Making sure the boot menu brought enough options.", "Asking BIOS time which time zone it lives in.", "Reviewing firmware with no intention of hurting its feelings.",
		"Asking the removable drive not to take that personally.", "Checking whether FAT32 is still doing its best.", "Giving the USB port a clean connection record.", "Looking for the drive letter after it changed its name.", "Asking storage to show its capacity for teamwork.", "Checking free space without judging occupied space.", "Waiting for the removable drive to become emotionally available.", "Asking the folder structure to stand up straight.", "Checking whether safely remove feels safe yet.", "Giving the logs folder something to talk about.",
		"Asking the SSD to keep this moving.", "Checking if the file system has filed everything.", "Making sure the root folder has strong roots.", "Asking the archive to compress its feelings.", "Counting drives before they change letters again.", "Asking the service tag to say that again more clearly.", "Checking whether the warranty still believes in us.", "Giving the asset tag a moment in the spotlight.", "Looking up coverage without bringing an umbrella.", "Asking the CLI to call home about the warranty.",
		"Checking if the service tag matches its name badge.", "Waiting for entitlement data to feel entitled.", "Asking the warranty date not to live in the past.", "Making sure the asset number has all its assets.", "Checking the model before it becomes last year's model.", "Giving local warranty data the first word.", "Asking the online lookup to use complete sentences.", "Checking whether support coverage covers support.", "Introducing the serial number to the service tag.", "Making warranty information earn its header space.",
		"Preparing five checkboxes for a very important click.", "Checking the checks that check the other checks.", "Asking Pass and Fail to remain professional.", "Making sure every section reaches a conclusion.", "Giving incomplete tests a little constructive pressure.", "Checking whether QA is short for Quite Accurate.", "Turning technician choices into neatly saved facts.", "Asking the final checks how final they feel.", "Making sure completed means completed this time.", "Counting green checks without celebrating too early.",
		"Giving each test the same fair trial.", "Checking the laptop from top case to bottom line.", "Asking the QA session to stay on this laptop.", "Making consistency look suspiciously easy.", "Preparing a tiny celebration with proper change control.", "Teaching the QA sheet to fit on one page.", "Checking whether the printer is accepting visitors.", "Giving the report margins healthy boundaries.", "Asking the PDF to remain portable.", "Making the notes section look intentionally placed.",
		"Checking if zoom can see the bigger picture.", "Giving Print a chance to make a good impression.", "Asking the page layout to keep it together.", "Turning test results into something management can admire.", "Checking whether the footer knows where the bottom is.", "Giving the PNG a self-contained sense of purpose.", "Asking the print queue to form an orderly queue.", "Making sure the report title has title insurance.", "Checking if the paper size agrees with the paper.", "Polishing the QA sheet until the pixels squeak.",
		"Asking the cache what it remembers about yesterday.", "Checking whether Config saved the configuration.", "Giving factory settings a map back to the factory.", "Making sure the technician name stays attached to the technician.", "Asking shared settings to share nicely.", "Checking whether the theme remembers its mood.", "Giving defaults a second chance to be default.", "Asking cached data not to impersonate the current laptop.", "Checking if Save actually saved us some time.", "Making the Windows and Mac settings compare notes.",
		"Asking the configuration file to remain writable.", "Checking timestamps for the latest version of the truth.", "Giving local storage and removable storage matching outfits.", "Asking the reset button to reset only what it means.", "Making sure the cache knows when to let go.", "Counting USB ports without unplugging the universe.", "Asking the processor to process that request.", "Checking memory for something memorable.", "Giving the motherboard credit for holding everything together.", "Asking the fan to circulate a memo.",
		"Checking whether Bluetooth is feeling connected.", "Giving the dock a moment to get its ports in a row.", "Asking the touch screen to stay in touch.", "Checking the chassis for character development.", "Giving the webcam, speakers, and ports a group assignment.", "Asking the CPU not to overthink the inventory.", "Checking RAM without bringing a shepherd.", "Giving the GPU something constructive to render.", "Asking the cooling system to remain cool.", "Making sure every port has a port of call.",
		"Reproducing the issue until it feels seen.", "Checking whether it only happens when someone is watching.", "Asking have you restarted politely and without accusation.", "Giving the error message a chance to apologize.", "Looking for the setting behind the other setting.", "Checking the cable before blaming the cloud.", "Asking the user story for a surprise ending.", "Making the workaround feel less like permanent architecture.", "Checking whether known issue means everybody knows.", "Giving the ticket enough detail to survive reassignment.",
		"Asking the bug to hold still for one screenshot.", "Checking if the fix fixed more than requested.", "Turning it works on my machine into useful evidence.", "Asking the laptop what changed since it worked.", "Making the root cause less root and more cause.", "Moving the progress bar with purpose and plausible deniability.", "Checking whether almost done has a legal definition.", "Giving the spinner a reasonable retirement plan.", "Waiting efficiently in several parallel tasks.", "Asking the loading screen to keep the audience warm.",
		"Checking if patience is installed and up to date.", "Making two seconds feel professionally productive.", "Giving the startup timer something worth timing.", "Asking the percentage to commit to a direction.", "Checking whether the last ten percent is unionized.", "Letting the progress bar build dramatic tension.", "Asking loading to finish loading the loading.", "Checking the clock without making it nervous.", "Giving background work a foreground compliment.", "Making wait time earn at least one smile.",
		"Asking Intune to tune in.", "Checking whether the old user has left the building.", "Giving device compliance a very compliant checklist.", "Asking encryption to keep everything under wraps.", "Checking whether the policy arrived before the laptop retired.", "Giving the group tag a well-defined group.", "Asking the management profile to manage expectations.", "Checking if the device object knows which device it is.", "Making sure removal removed the right thing.", "Asking access control to control itself.",
		"Checking whether the token has excellent expiration manners.", "Giving least privilege the most respect.", "Asking authentication to prove it is authentication.", "Checking if compliance and reality are synchronized.", "Making security boring in the best possible way.", "Scheduling a brief meeting between hardware and software.", "Checking whether this could have been an email.", "Giving the laptop a performance review with fewer forms.", "Asking the spreadsheet to stay out of the UI.", "Making the status update more status and less update.",
		"Checking if the deadline has moved closer again.", "Giving the workflow one fewer place to improvise.", "Asking consistency to become company policy.", "Checking whether the process has a process owner.", "Making the handoff less like a scavenger hunt.", "Giving the checklist a promotion to standard practice.", "Asking the documentation to document itself.", "Checking whether the meeting room has the correct adapter.", "Making repeatable work slightly less repetitive.", "Giving future technicians fewer surprises.",
		"Asking the laptop why it only does that on Tuesdays.", "Checking whether the hinge has strong opinions.", "Giving the laptop a chance to tell its side.", "Asking the fan if everything is really fine.", "Checking whether the sleep button needs a nap.", "Giving the trackpad some space to express itself.", "Asking the laptop to bring its whole self to QA.", "Checking if the chassis woke up grumpy.", "Giving the status lights a chance to communicate.", "Asking the laptop not to make this weird.",
		"Checking whether the beep was informational or judgmental.", "Giving the device one final opportunity to cooperate.", "Asking the laptop to save the drama for the diagnostics log.", "Checking if the machine has any questions for us.", "Making sure this laptop leaves better than it arrived."
	};

	private const int EnumCurrentSettings = -1;

	private const int DisplayDeviceAttachedToDesktop = 1;

	private const int DisplayDevicePrimaryDevice = 4;

	private const int WmDeviceChange = 537;

	private const int DbtDeviceArrival = 32768;

	private const int DbtDeviceRemoveComplete = 32772;

	private const int DbtDeviceNodesChanged = 7;

	private const string SecureBootWriteScript = "function statusText($s) {\n  switch ([string]$s) { '0' { 'Success'; break } '1' { 'Failed'; break } '2' { 'Invalid parameter'; break } '3' { 'Access denied'; break } '4' { 'Not supported'; break } default { [string]$s } }\n}\n$names = @('SecureBoot','SecureBootEnable','Secure Boot','Secure Boot Enable')\n$values = @('Enabled','Enable','On','1')\n$messages = New-Object System.Collections.Generic.List[string]\ntry {\n  $attrs = @(Get-CimInstance -Namespace 'root\\dcim\\sysman\\biosattributes' -ClassName 'EnumerationAttribute' -ErrorAction Stop)\n  foreach ($attr in $attrs) { $n = [string]$attr.AttributeName; if ($n -match '(?i)secure\\s*boot|secureboot') { $names = @($n) + $names } }\n} catch { $messages.Add(\"Lookup failed: $($_.Exception.Message)\") | Out-Null }\nforeach ($name in ($names | Select-Object -Unique)) {\n  foreach ($value in $values) {\n    try {\n      $iface = Get-CimInstance -Namespace 'root\\dcim\\sysman\\biosattributes' -ClassName 'BIOSAttributeInterface' -ErrorAction Stop | Select-Object -First 1\n      if ($iface) {\n        $result = Invoke-CimMethod -InputObject $iface -MethodName SetAttribute -Arguments @{ SecType=[uint32]0; SecHndCount=[uint32]0; SecHandle=[byte[]]@(); AttributeName=$name; AttributeValue=$value } -ErrorAction Stop\n        $status = statusText $result.Status\n        if ($status -eq 'Success') { exit 0 }\n        $messages.Add(\"$name=$value returned $status\") | Out-Null\n      }\n    } catch { $messages.Add(\"$name=$value failed: $($_.Exception.Message)\") | Out-Null }\n  }\n}\nthrow (($messages | Select-Object -First 8) -join '; ')";

	private string HashDir => Path.Combine(_dataRoot, "hash");

	private string QaDir => Path.Combine(_dataRoot, "QA sheets");

	private string HardwareDir => Path.Combine(_dataRoot, "hardware");

	private string LogsDir => Path.Combine(_dataRoot, "logs");

	private string ActivityDir => Path.Combine(_dataRoot, "activity");

	private string RuntimeDir => Path.Combine(_dataRoot, ".runtime");

	private string StartupJokeStatePath => Path.Combine(RuntimeDir, "startup-joke-state.json");

	private string LegacyStartupJokeStatePath => Path.Combine(RuntimeDir, "startup-joke-index.txt");

	private string QaSessionCachePath => Path.Combine(RuntimeDir, "qa-session.json");

	private string QaSessionArchiveDir => Path.Combine(RuntimeDir, "sessions");

	private string QaSessionIndexPath => Path.Combine(RuntimeDir, "sessions-index.json");

	private string ConfigPath => Path.Combine(_dataRoot, "Laptop-QA-Config.json");

	private string CctkExe => Path.Combine(_appRoot, "tools", "cctk", "cctk.exe");

	private string AudioScript => Path.Combine(_appRoot, "tools", "Pnp-AudioDevices.ps1");

	private string AutopilotHashScript => Path.Combine(_appRoot, "tools", "Get-WindowsAutoPilotInfo.ps1");

	private string CommandPowerManagerDir => Path.Combine(_appRoot, "tools", "CommandPowerManager");

	private static string DellOptimizerCliPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dell", "DellOptimizer", "do-cli.exe");

	private static string BcdEditPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "bcdedit.exe");

	private static string ShutdownExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "shutdown.exe");

	#endregion

	#region Window lifecycle, configuration, theming, and startup

	public MainWindow()
	{
		InitializeComponent();
		_qaSessionSaveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(400)
		};
		_qaSessionSaveTimer.Tick += delegate
		{
			_qaSessionSaveTimer.Stop();
			SaveQaSessionCache();
		};
		AddHandler(Button.ClickEvent, new RoutedEventHandler(AnyAppInteraction_Changed), handledEventsToo: true);
		base.Closing += MainWindow_Closing;
		base.Activated += MainWindow_Activated;
		base.SourceInitialized += delegate
		{
			AttachUsbDeviceChangeHook();
		};
		_details["WiFi"] = "Looking for a connected Wi-Fi IP or visible SSIDs.";
		_details["Ethernet"] = "Looking for at least one physical Ethernet adapter that is Up.";
		_details["Camera"] = "Start Camera, then choose Pass or Fail.";
		_details["ExternalVideo"] = "Verify video output on the external display.";
		_details["Keyboard"] = "Start tester, then choose Pass or Fail.";
		_details["Diagnostics"] = "DellPrebootDiagnosticsLog.txt was not found on the small FAT32 diagnostics drive.";
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		_headerClockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1)
		};
		_headerClockTimer.Tick += HeaderClockTimer_Tick;
		UpdateHeaderDateTime();
		_headerClockTimer.Start();
		StartCurrentBatteryPolling();
		EnsureFolders();
		_config = LoadConfig();
		if (File.Exists(ConfigPath))
		{
			_configWriteUtc = File.GetLastWriteTimeUtc(ConfigPath);
		}
		LanguageCatalog.ApplyCulture(_config.AppLanguage);
		ApplyAppTheme(_config.AppTheme);
		WpfLocalization.Apply(this, _config.AppLanguage);
		SetWindowFitToScreen();
		BringStartupSplashToFront();
		await SetStartupSplashStatusAsync(NextStartupJokeForLaunch());
		PromptForTechnicianNameIfNeeded();
		HeaderTechnician.Text = L(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "Technician: not set" : ("Technician: " + _config.TechnicianName));
		AddActivity("System", "Laptop QA Testing  started.");
		AddActivity("System", "App folder ready: " + _appRoot);
		AddActivity("System", "Data folder ready: " + _dataRoot);
		CleanupOldFiles(HashDir, 90, "Hash", "hash file(s)");
		CleanupOldFiles(QaDir, 90, "QA Sheet", "QA sheet file(s)");
		CleanupOldFiles(LogsDir, 90, "Logs", "log file(s)", recursive: true);
		CleanupOldFiles(ActivityDir, 90, "Activity", "activity log file(s)", recursive: true);
		CleanupOldFiles(HardwareDir, 90, "Hardware", "hardware snapshot(s)");
		CleanupCachedSessions();
		CleanupDiagnosticsSourceArchives();
		CleanupQaSheetHtmlFiles();
		CleanupEdgeQaProfiles();
		QaSessionCache? cachedSession = ReadQaSessionCache();
		if (cachedSession != null && ShouldSkipStartupRefresh(cachedSession))
		{
			RestoreQaSessionCache(cachedSession);
			await ApplyStartupCurrentBatteryAsync(GetCurrentBatterySnapshotAsync());
			SaveQaSessionCache();
			_completionCelebrated = IsQaComplete();
			_qaSessionReady = true;
			RefreshCachedSessionPicker();
			SetSummaryStatus("Ready");
			AddActivity("System", "Startup data refresh skipped because the saved QA session has not been reset.");
			await Task.Delay(450);
			await HideStartupSplashAsync();
			base.Topmost = false;
			ShowRemovableDriveWarningIfNeeded();
			return;
		}
		_suppressQaSessionCache = true;
		try
		{
			await LoadInitialDataAsync();
		}
		finally
		{
			_suppressQaSessionCache = false;
		}
		if (cachedSession != null)
		{
			RestoreQaSessionCache(cachedSession);
		}
		SaveQaSessionCache();
		ErrorLog.StartSession(CachedFileIdentifier());
		_completionCelebrated = IsQaComplete();
		_qaSessionReady = true;
		RefreshCachedSessionPicker();
		ShowRemovableDriveWarningIfNeeded();
	}

	private void ShowRemovableDriveWarningIfNeeded()
	{
		if (!_removableDataDriveDetected && !_removableDriveWarningShown)
		{
			_removableDriveWarningShown = true;
			AddActivity("Storage", "No Laptop QA removable drive was detected. The app is using computer-local storage.");
			MessageBox.Show(this, "No Laptop QA removable drive was detected. The app is using storage on this computer.\n\nConnect a drive containing Laptop-QA-Drive.json and the LAPTOP QA folder, then close and reopen the app before continuing QA work.", "Removable Drive Not Detected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void EnsureFolders()
	{
		Directory.CreateDirectory(HashDir);
		Directory.CreateDirectory(QaDir);
		Directory.CreateDirectory(HardwareDir);
		Directory.CreateDirectory(LogsDir);
		Directory.CreateDirectory(ActivityDir);
		Directory.CreateDirectory(RuntimeDir);
		Directory.CreateDirectory(QaSessionArchiveDir);
	}

	private static string ResolveDataRoot()
	{
		string? text = GetDataRootFromArgs() ?? Environment.GetEnvironmentVariable("LAPTOP_QA_DATA_ROOT");
		if (!string.IsNullOrWhiteSpace(text))
		{
			try
			{
				string fullPath = Path.GetFullPath(text);
				if (Directory.Exists(fullPath))
				{
					return fullPath;
				}
			}
			catch
			{
			}
		}
		string? text2 = FindPreferredRemovableDataRoot();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return AppContext.BaseDirectory;
	}

	private static string? FindPreferredRemovableDataRoot()
	{
		try
		{
			return (from candidate in (from DataRootCandidate candidate in from candidate in DriveInfo.GetDrives().Select<DriveInfo, DataRootCandidate?>(delegate(DriveInfo drive)
						{
							try
							{
								if (!drive.IsReady)
								{
									return null;
								}
								string fullName = drive.RootDirectory.FullName;
								string text = Path.Combine(fullName, "LAPTOP QA");
								if (!IsPackagedDataRoot(text))
								{
									return null;
								}
								bool flag = File.Exists(Path.Combine(fullName, "Laptop-QA-Drive.json"));
								bool flag2 = drive.DriveType == DriveType.Removable || string.Equals(drive.VolumeLabel, "IT SUPP", StringComparison.OrdinalIgnoreCase);
								if (!flag && !flag2)
								{
									return null;
								}
								string path = Path.Combine(text, ".runtime", "qa-session.json");
								DateTime sessionWriteUtc = (File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue);
								return new DataRootCandidate(Path.GetFullPath(text), flag, sessionWriteUtc);
							}
							catch
							{
								return null;
							}
						})
						where (object)candidate != null
						select candidate
					orderby candidate.HasMarker descending, candidate.SessionWriteUtc descending
					select candidate).ThenBy<DataRootCandidate, string>((DataRootCandidate candidate) => candidate.Path, StringComparer.OrdinalIgnoreCase)
				select candidate.Path).FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	private static bool IsPackagedDataRoot(string path)
	{
		if (!Directory.Exists(path) || !string.Equals(Path.GetFileName(path.TrimEnd(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		})), "LAPTOP QA", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!Directory.Exists(Path.Combine(path, ".runtime")) && !Directory.Exists(Path.Combine(path, "App")))
		{
			return File.Exists(Path.Combine(path, "Laptop-QA-Config.json"));
		}
		return true;
	}

	private static string? GetDataRootFromArgs()
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 1; i < commandLineArgs.Length; i++)
		{
			if (commandLineArgs[i].Equals("--data-root", StringComparison.OrdinalIgnoreCase) && i + 1 < commandLineArgs.Length)
			{
				return commandLineArgs[i + 1];
			}
			if (commandLineArgs[i].StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
			{
				return commandLineArgs[i].Substring("--data-root=".Length);
			}
		}
		return null;
	}

	private AppConfig LoadConfig()
	{
		try
		{
			if (!File.Exists(ConfigPath))
			{
				return new AppConfig();
			}
			return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions()) ?? new AppConfig();
		}
		catch
		{
			return new AppConfig();
		}
	}

	private void SaveConfig(AppConfig config)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? _dataRoot);
		JsonObject jsonObject;
		try
		{
			jsonObject = (File.Exists(ConfigPath) ? ((JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject) ?? new JsonObject()) : new JsonObject());
		}
		catch
		{
			jsonObject = new JsonObject();
		}
		foreach (KeyValuePair<string, JsonNode?> item in (JsonSerializer.SerializeToNode(config, JsonOptions()) as JsonObject) ?? new JsonObject())
		{
			jsonObject[item.Key] = item.Value?.DeepClone();
		}
		string text = ConfigPath + $".{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(text, jsonObject.ToJsonString(JsonOptions()), Encoding.UTF8);
			File.Move(text, ConfigPath, overwrite: true);
			_configWriteUtc = File.GetLastWriteTimeUtc(ConfigPath);
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	private void PromptForTechnicianNameIfNeeded()
	{
		if (string.IsNullOrWhiteSpace(_config.TechnicianName))
		{
			TechnicianNameWindow technicianNameWindow = new TechnicianNameWindow(this, _currentTheme, _config.AppLanguage);
			if (technicianNameWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(technicianNameWindow.TechnicianName))
			{
				_config.TechnicianName = technicianNameWindow.TechnicianName.Trim();
				SaveConfig(_config);
				AddActivity("Onboarding", "Technician name saved: " + _config.TechnicianName);
			}
		}
	}

	private static JsonSerializerOptions JsonOptions()
	{
		return new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNameCaseInsensitive = true
		};
	}

	private void ApplyAppTheme(string? theme)
	{
		ThemePalette themePalette = ThemePalette.For(NormalizeTheme(theme));
		bool flag = themePalette.Name == "Light";
		bool flag2 = themePalette.Name == "AMOLED";
		_currentTheme = themePalette.Name;
		_config.AppTheme = themePalette.Name;
		SetSolidResource("TextBrush", themePalette.Text);
		SetSolidResource("MutedBrush", themePalette.Muted);
		SetSolidResource("AccentBrush", themePalette.Accent);
		SetSolidResource("PanelStroke", themePalette.PanelStroke);
		SetSolidResource("InputStrokeBrush", themePalette.InputStroke);
		SetSolidResource("ActivityPanelBrush", themePalette.ActivityPanel);
		SetSolidResource("SplashOverlayBrush", themePalette.SplashOverlay);
		SetSolidResource("NoteInputBrush", themePalette.NoteInput);
		SetSolidResource("PrimaryButtonBrush", themePalette.PrimaryButton);
		SetSolidResource("ResetButtonBrush", themePalette.ResetButton);
		SetSolidResource("DangerButtonBrush", themePalette.DangerButton);
		SetSolidResource("OkButtonBrush", flag ? "#2F855A" : (flag2 ? "#145C3A" : "#19734A"));
		SetSolidResource("PowerButtonBrush", flag ? "#EEF4F2" : (flag2 ? "#151515" : "#314852"));
		SetSolidResource("PowerButtonBorderBrush", flag ? "#C7D5D7" : (flag2 ? "#3A3A3A" : "#4E6972"));
		SetSolidResource("FinalCheckCheckedBrush", themePalette.FinalCheckChecked);
		SetSolidResource("FinalCheckCheckedBoxBrush", themePalette.FinalCheckCheckedBox);
		SetSolidResource("FinalCheckCheckMarkBrush", themePalette.FinalCheckCheckMark);
		SetSolidResource("FinalCheckUncheckedBrush", flag ? "#EAF0EF" : (flag2 ? "#FF050505" : "#241D3038"));
		SetSolidResource("FinalCheckHoverBrush", flag ? "#DCEBE8" : (flag2 ? "#FF151515" : "#34173D43"));
		SetSolidResource("FinalCheckBorderBrush", flag ? "#8EA4A8" : (flag2 ? "#FF555555" : "#6682949B"));
		SetSolidResource("CachedSessionHoverBrush", flag ? "#60757E" : (flag2 ? "#303030" : "#60757E"));
		SetSolidResource("CachedSessionSelectedBrush", flag ? "#203741" : (flag2 ? "#242424" : "#263D46"));
		SetSolidResource("CachedSessionTextBrush", themePalette.Text);
		SetSolidResource("CachedSessionSelectedTextBrush", "#FFFFFF");
		SetSolidResource("SectionSurfaceBrush", flag ? "#FFF9FAF7" : (flag2 ? "#FF0A0A0A" : "#FF465B63"));
		SetGradientResource("ShellBrush", themePalette.Shell);
		SetGradientResource("GlassPanelBrush", themePalette.GlassPanel);
		SetGradientResource("DarkGlassBrush", themePalette.DarkGlass);
		SetGradientResource("UtilityPanelBrush", themePalette.UtilityPanel);
		Brush sharedSectionSurfaceBrush = (Brush)base.Resources["SectionSurfaceBrush"];
		UsbPortTestPanel.Background = sharedSectionSurfaceBrush;
		FinalChecksPanel.Background = sharedSectionSurfaceBrush;
		QaOutputPanel.Background = sharedSectionSurfaceBrush;
		BiosSettingsPanel.Background = sharedSectionSurfaceBrush;
		WavePathBack.Fill = BrushFromHex(themePalette.WaveBack);
		WavePathFront.Fill = BrushFromHex(themePalette.WaveFront);
		WavePathBack.Data = Geometry.Parse("M -20,440 C 190,370 380,550 580,470 C 760,400 980,555 1300,465 L1300,740 L-20,740 Z");
		WavePathFront.Data = Geometry.Parse("M -20,500 C 230,430 420,615 680,525 C 880,455 1080,600 1300,515 L1300,740 L-20,740 Z");
		WavePathBack.Opacity = 0.75;
		WavePathFront.Opacity = 0.7;
		PowerMenuPanel.Background = BrushFromHex(themePalette.ActivityPanel);
		PowerMenuPanel.BorderBrush = BrushFromHex(themePalette.PanelStroke);
		ActivityPanel.Background = BrushFromHex(themePalette.ActivityPanel);
		HardwarePanel.Background = BrushFromHex(themePalette.ActivityPanel);
		FoldersPanel.Background = BrushFromHex(themePalette.ActivityPanel);
		SheetNotesPanel.Background = BrushFromHex(flag ? "#FFFFFFFF" : (flag2 ? "#FF030303" : "#FF334D57"));
		RmaIssueInputBorder.Background = BrushFromHex(flag ? "#FFFAFAF6" : (flag2 ? "#FF080808" : "#FF1D3038"));
		RepairNotesInputBorder.Background = BrushFromHex(flag ? "#FFFAFAF6" : (flag2 ? "#FF080808" : "#FF1D3038"));
		ActivityLogBorder.Background = BrushFromHex(themePalette.NoteInput);
		HardwareDetailsBorder.Background = BrushFromHex(themePalette.NoteInput);
		SolidColorBrush foreground = BrushFromHex(flag ? "#18333D" : themePalette.DrawerForeground);
		ActivityDrawerButton.Foreground = foreground;
		ActivityDrawerButton.Background = BrushFromHex(flag ? "#B8C8CB" : (flag2 ? "#2E2E2E" : "#526973"));
		NotesDrawerButton.Foreground = foreground;
		NotesDrawerButton.Background = BrushFromHex(flag ? "#C5D5D2" : (flag2 ? "#383838" : "#607982"));
		HardwareButton.Foreground = foreground;
		HardwareButton.Background = BrushFromHex(flag ? "#AFC1C5" : (flag2 ? "#242424" : "#49636C"));
		FoldersDrawerButton.Foreground = foreground;
		FoldersDrawerButton.Background = BrushFromHex(flag ? "#D1DDD9" : (flag2 ? "#303030" : "#6B858E"));
		UpdateDrawerTabBorders();
		if (FinalChecksPanel.Background is LinearGradientBrush || base.Resources["SectionSurfaceBrush"] is SolidColorBrush)
		{
			FinalChecksPanel.Background = (Brush)base.Resources["SectionSurfaceBrush"];
		}
		if (Shell.Effect is DropShadowEffect dropShadowEffect)
		{
			dropShadowEffect.Color = ColorFromHex(themePalette.ShellShadow);
			dropShadowEffect.Opacity = (flag ? 0.22 : (flag2 ? 0.56 : 0.36));
			dropShadowEffect.BlurRadius = 84.0;
			dropShadowEffect.ShadowDepth = 8.0;
			dropShadowEffect.Direction = 315.0;
		}
		if (PowerMenuPanel.Effect is DropShadowEffect dropShadowEffect2)
		{
			dropShadowEffect2.Color = ColorFromHex(flag ? "#657A80" : (flag2 ? "#000000" : "#0B2028"));
			dropShadowEffect2.Opacity = (flag ? 0.22 : (flag2 ? 0.42 : 0.24));
		}
		if (HeaderAssetBubble != null && HeaderAsset != null && !HeaderAsset.Text.Contains("loading", StringComparison.OrdinalIgnoreCase))
		{
			UpdateAssetHeader();
		}
		SetBiosStatusIcon(_states.TryGetValue("SecureBoot", out string? value) ? value : "Unknown");
		RefreshStepIconBrushes();
		if (CurrentBatteryPanel != null)
		{
			UpdateCurrentBatteryDisplay();
		}
		if (UsbPortIndicatorsPanel != null)
		{
			UpdateUsbPortUi();
		}
	}

	private void SetWindowFitToScreen()
	{
		Rect workArea = SystemParameters.WorkArea;
		double num = Math.Max(1.0, workArea.Width);
		double val = Math.Min(val2: Math.Max(1.0, workArea.Height) / 720.0, val1: num / 1280.0);
		double num2 = Math.Max(0.5, val);
		base.Width = Math.Round(1280.0 * num2);
		base.Height = Math.Round(720.0 * num2);
		base.WindowStartupLocation = WindowStartupLocation.Manual;
		base.Left = Math.Round(workArea.Left + (workArea.Width - base.Width) / 2.0);
		base.Top = Math.Round(workArea.Top + (workArea.Height - base.Height) / 2.0);
		if (Math.Abs(num2 - 1.0) > 0.001)
		{
			AddActivity("System", $"Window scaled to {num2:P0} to fill this display's usable work area while preserving its proportions.");
		}
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

	private void SetSolidResource(string key, string hex)
	{
		Color color = ColorFromHex(hex);
		if (base.Resources[key] is SolidColorBrush { IsFrozen: false } solidColorBrush)
		{
			solidColorBrush.Color = color;
		}
		else
		{
			base.Resources[key] = new SolidColorBrush(color);
		}
	}

	private void SetGradientResource(string key, IReadOnlyList<string> colors)
	{
		if (!(base.Resources[key] is LinearGradientBrush linearGradientBrush))
		{
			base.Resources[key] = CreateGradientBrush(colors);
			return;
		}
		if (linearGradientBrush.IsFrozen)
		{
			base.Resources[key] = CreateGradientBrush(colors, linearGradientBrush);
			return;
		}
		for (int i = 0; i < linearGradientBrush.GradientStops.Count && i < colors.Count; i++)
		{
			linearGradientBrush.GradientStops[i].Color = ColorFromHex(colors[i]);
		}
	}

	private static LinearGradientBrush CreateGradientBrush(IReadOnlyList<string> colors, LinearGradientBrush? source = null)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = (source?.StartPoint ?? new Point(0.0, 0.0)),
			EndPoint = (source?.EndPoint ?? new Point(1.0, 1.0))
		};
		for (int i = 0; i < colors.Count; i++)
		{
			double offset = ((source != null && i < source.GradientStops.Count) ? source.GradientStops[i].Offset : ((colors.Count == 1) ? 0.0 : ((double)i / (double)(colors.Count - 1))));
			linearGradientBrush.GradientStops.Add(new GradientStop(ColorFromHex(colors[i]), offset));
		}
		return linearGradientBrush;
	}

	private static SolidColorBrush BrushFromHex(string hex)
	{
		return new SolidColorBrush(ColorFromHex(hex));
	}

	private static Color ColorFromHex(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.Transparent));
	}

	private Brush StepBrush(string state)
	{
		return state switch
		{
			"Ok" => BrushFromHex((_currentTheme == "Light") ? "#12633D" : ((_currentTheme == "AMOLED") ? "#E0E0E0" : "#A7F3D0")),
			"Bad" => BrushFromHex((_currentTheme == "Light") ? "#9B3036" : ((_currentTheme == "AMOLED") ? "#A8A8A8" : "#FCA5A5")),
			"Ignored" => BrushFromHex((_currentTheme == "Light") ? "#52666F" : ((_currentTheme == "AMOLED") ? "#BDBDBD" : "#B9C7CB")),
			"Warning" => BrushFromHex((_currentTheme == "Light") ? "#8A5B00" : ((_currentTheme == "AMOLED") ? "#C8C8C8" : "#F2C75B")),
			"Working" => BrushFromHex((_currentTheme == "Light") ? "#102D39" : ((_currentTheme == "AMOLED") ? "#D0D0D0" : "#5EEAD4")),
			_ => (Brush)FindResource("MutedBrush"),
		};
	}

	private Brush BiosButtonBrush(string state)
	{
		return state switch
		{
			"Ok" => BrushFromHex("#12633D"),
			"Bad" => BrushFromHex("#9B3036"),
			"Working" => BrushFromHex((_currentTheme == "Light") ? "#2F6F68" : ((_currentTheme == "AMOLED") ? "#707070" : "#5EEAD4")),
			_ => (Brush)FindResource("PrimaryButtonBrush"),
		};
	}

	private async Task LoadInitialDataAsync(bool showStartupSplash = true)
	{
		Stopwatch startupTimer = Stopwatch.StartNew();
		DateTime minimumSplashUntil = (showStartupSplash ? DateTime.UtcNow.AddMilliseconds(2200.0) : DateTime.UtcNow);
		BeginProcessing("Startup");
		try
		{
			_ = 1;
			try
			{
				if (!showStartupSplash)
				{
					AddActivity("System", "Refreshing device data for the new QA session.");
				}
				Task<Dictionary<string, string>> headerAsync = GetHeaderAsync();
				Task<string> batteryTask = GetBatterySummaryAsync();
				Task<CurrentBatterySnapshot> currentBatteryTask = GetCurrentBatterySnapshotAsync();
				Task<HardwareSnapshot> hardwareTask = GetHardwareSnapshotAsync();
				Task biosTask = RefreshBiosAsync();
				await ApplyStartupHeaderAsync(headerAsync);
				Task<DiagnosticsResult> diagnosticsResultAsync = GetDiagnosticsResultAsync();
				Task task = RefreshWarrantyAsync();
				InlineArray6<Task> buffer = default(InlineArray6<Task>);
				buffer[0] = ApplyStartupBatteryAsync(batteryTask);
				buffer[1] = ApplyStartupCurrentBatteryAsync(currentBatteryTask);
				buffer[2] = ApplyStartupHardwareAsync(hardwareTask);
				buffer[3] = ApplyStartupDiagnosticsAsync(diagnosticsResultAsync);
				buffer[4] = AwaitStartupStepAsync("BIOS", biosTask);
				buffer[5] = AwaitStartupStepAsync("Warranty", task);
				await Task.WhenAll(buffer);
				AddActivity("System", $"Startup checks completed in {startupTimer.Elapsed.TotalSeconds:0.0}s with parallel loading.");
			}
			catch (Exception ex)
			{
				AddActivity("System", "Startup data load failed: " + ex.Message);
			}
		}
		finally
		{
			if (showStartupSplash)
			{
				TimeSpan timeSpan = minimumSplashUntil - DateTime.UtcNow;
				if (timeSpan > TimeSpan.Zero)
				{
					await Task.Delay(timeSpan);
				}
				await HideStartupSplashAsync();
			}
			base.Topmost = false;
			EndProcessing("Startup");
		}
	}

	private string NextStartupJokeForLaunch()
	{
		try
		{
			Directory.CreateDirectory(RuntimeDir);
			StartupJokeDeckState? startupJokeDeckState = null;
			if (File.Exists(StartupJokeStatePath))
			{
				try
				{
					startupJokeDeckState = JsonSerializer.Deserialize<StartupJokeDeckState>(File.ReadAllText(StartupJokeStatePath));
				}
				catch
				{
					startupJokeDeckState = null;
				}
			}
			if (startupJokeDeckState == null || startupJokeDeckState.JokeCount != StartupLoadingJokes.Length || startupJokeDeckState.Order.Count != StartupLoadingJokes.Length || startupJokeDeckState.Order.Distinct().Count() != StartupLoadingJokes.Length || !startupJokeDeckState.Order.All((int index) => index >= 0 && index < StartupLoadingJokes.Length) || startupJokeDeckState.Position < 0 || startupJokeDeckState.Position > StartupLoadingJokes.Length)
			{
				int previousIndex = -1;
				if (File.Exists(LegacyStartupJokeStatePath) && int.TryParse(File.ReadAllText(LegacyStartupJokeStatePath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					previousIndex = (Math.Clamp(result, 0, StartupLoadingJokes.Length - 1) - 1 + StartupLoadingJokes.Length) % StartupLoadingJokes.Length;
				}
				startupJokeDeckState = CreateShuffledJokeDeck(previousIndex);
			}
			else if (startupJokeDeckState.Position >= startupJokeDeckState.Order.Count)
			{
				startupJokeDeckState = CreateShuffledJokeDeck(startupJokeDeckState.LastIndex);
			}
			int num = startupJokeDeckState.Order[startupJokeDeckState.Position];
			startupJokeDeckState.Position++;
			startupJokeDeckState.LastIndex = num;
			SaveStartupJokeDeckState(startupJokeDeckState);
			return StartupLoadingJokes[num];
		}
		catch
		{
			return StartupLoadingJokes[Random.Shared.Next(StartupLoadingJokes.Length)];
		}
	}

	private static StartupJokeDeckState CreateShuffledJokeDeck(int previousIndex)
	{
		List<int> list = Enumerable.Range(0, StartupLoadingJokes.Length).ToList();
		for (int num = list.Count - 1; num > 0; num--)
		{
			int @int = RandomNumberGenerator.GetInt32(num + 1);
			List<int> list2 = list;
			int index = num;
			int index2 = @int;
			int value = list[@int];
			int value2 = list[num];
			list2[index] = value;
			list[index2] = value2;
		}
		if (list.Count > 1 && list[0] == previousIndex)
		{
			int num2 = list.FindIndex(1, (int candidate) => candidate != previousIndex);
			if (num2 > 0)
			{
				List<int> list2 = list;
				int index2 = num2;
				int index = list[num2];
				int value2 = list[0];
				list[0] = index;
				list2[index2] = value2;
			}
		}
		return new StartupJokeDeckState
		{
			JokeCount = StartupLoadingJokes.Length,
			Order = list,
			Position = 0,
			LastIndex = previousIndex
		};
	}

	private void SaveStartupJokeDeckState(StartupJokeDeckState state)
	{
		string contents = JsonSerializer.Serialize(state);
		string text = StartupJokeStatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, contents);
			File.Move(text, StartupJokeStatePath, overwrite: true);
		}
		catch
		{
			try
			{
				File.WriteAllText(StartupJokeStatePath, contents);
			}
			finally
			{
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
				}
				catch
				{
				}
			}
		}
	}

	private async Task ApplyStartupHeaderAsync(Task<Dictionary<string, string>> task)
	{
		try
		{
			Dictionary<string, string> dictionary = await task;
			_serviceTag = dictionary.GetValueOrDefault("Serial", "Unknown");
			_assetTag = dictionary.GetValueOrDefault("Asset", "Unknown");
			HeaderSerial.Text = L("Service Tag: " + _serviceTag);
			UpdateAssetHeader();
			AddActivity("System", $"Device identity loaded: Service Tag = {_serviceTag}; Asset = {_assetTag}.");
		}
		catch (Exception ex)
		{
			_serviceTag = "Unknown";
			_assetTag = "Unknown";
			HeaderSerial.Text = L("Service Tag: Unknown");
			UpdateAssetHeader();
			AddActivity("System", "Device identity load failed: " + ex.Message);
		}
	}

	private void UpdateAssetHeader()
	{
		UpdateDeviceNameHeader();
		string text = (string.IsNullOrWhiteSpace(_assetTag) ? "None" : _assetTag.Trim());
		HeaderAsset.Text = L("Asset: " + text);
		if (IsMissingAssetTag(text))
		{
			HeaderAssetBubble.Background = BrushFromHex((_currentTheme == "Light") ? "#FDE2E4" : ((_currentTheme == "AMOLED") ? "#2A2A2A" : "#5B2028"));
			HeaderAssetBubble.BorderBrush = BrushFromHex((_currentTheme == "Light") ? "#C94C56" : ((_currentTheme == "AMOLED") ? "#A0A0A0" : "#FCA5A5"));
			HeaderAsset.Foreground = BrushFromHex((_currentTheme == "Light") ? "#842029" : ((_currentTheme == "AMOLED") ? "#F4F4F4" : "#FEE2E2"));
		}
		else
		{
			HeaderAssetBubble.Background = Brushes.Transparent;
			HeaderAssetBubble.BorderBrush = Brushes.Transparent;
			HeaderAsset.Foreground = (Brush)FindResource("MutedBrush");
		}
	}

	private static bool IsMissingAssetTag(string value)
	{
		string text = value.Trim();
		if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) && !string.Equals(text, "Unknown", StringComparison.OrdinalIgnoreCase) && !string.Equals(text, "N/A", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(text, "Not Available", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string HeaderBatteryValue(string summary)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return "unavailable";
		}
		return Regex.Replace(summary.Trim(), "^Battery Health:\\s*", "", RegexOptions.IgnoreCase);
	}

	private static string NormalizeBatteryHealthRating(string? value)
	{
		string input = value?.Trim() ?? "";
		if (Regex.IsMatch(input, "\\bexcellent\\b", RegexOptions.IgnoreCase))
		{
			return "Excellent";
		}
		if (Regex.IsMatch(input, "\\b(good|ok|okay|normal|healthy)\\b", RegexOptions.IgnoreCase))
		{
			return "Good";
		}
		if (Regex.IsMatch(input, "\\b(fair|warning|warn|degraded)\\b", RegexOptions.IgnoreCase))
		{
			return "Fair";
		}
		if (Regex.IsMatch(input, "\\b(poor|bad|critical|failed|failure|replace)\\b", RegexOptions.IgnoreCase))
		{
			return "Poor";
		}
		return "";
	}

	private static string BatteryHealthRatingFromDiagnostics(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return "";
		}
		Match match = Regex.Match(raw, "^\\s*\\[\\s*BATTERY\\s*\\]\\s*(?<body>.*?)(?=^\\s*\\[[^\\]]+\\]|\\z)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
		if (!match.Success)
		{
			return "";
		}
		Match match2 = Regex.Match(match.Groups["body"].Value, "^\\s*Health\\s*=\\s*(?<rating>.+?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
		if (!match2.Success)
		{
			return "";
		}
		return NormalizeBatteryHealthRating(match2.Groups["rating"].Value);
	}

	private static string BatteryHealthSummary(string? capacitySummary, string? logRating)
	{
		string text = NormalizeBatteryHealthRating(logRating);
		Match match = Regex.Match(capacitySummary ?? "", "(?<percent>\\d{1,3})\\s*%", RegexOptions.IgnoreCase);
		int result;
		string text2 = ((match.Success && int.TryParse(match.Groups["percent"].Value, out result)) ? $" ({Math.Clamp(result, 0, 100)}%)" : "");
		return "Battery Health: " + (string.IsNullOrWhiteSpace(text) ? "unavailable" : text) + text2;
	}

	private void UpdateBatteryHealthDisplay()
	{
		_batteryHealthRating = NormalizeBatteryHealthRating(_batteryHealthRating);
		_batterySummary = BatteryHealthSummary(_batterySummary, _batteryHealthRating);
		HeaderBattery.Text = L(_batterySummary);
		var (filled, text) = _batteryHealthRating switch
		{
			"Excellent" => (4, "#22C55E"),
			"Good" => (3, "#EAB308"),
			"Fair" => (2, "#F97316"),
			"Poor" => (1, "#EF4444"),
			_ => (0, ""),
		};
		HeaderBatteryDots.Text = string.Concat(from index in Enumerable.Range(0, 4)
			select (index >= filled) ? "\u25CB" : "\u25CF");
		HeaderBatteryDots.Foreground = (string.IsNullOrWhiteSpace(text) ? ((Brush)FindResource("MutedBrush")) : BrushFromHex(text));
		Match match = Regex.Match(_batterySummary, "(?<percent>\\d{1,3})\\s*%");
		string text2 = (match.Success ? (" Windows capacity health: " + match.Groups["percent"].Value + "%.") : "");
		string toolTip = (string.IsNullOrWhiteSpace(_batteryHealthRating) ? ("Dell diagnostics log battery rating unavailable." + text2) : ("Dell diagnostics log battery rating: " + _batteryHealthRating + "." + text2));
		HeaderBattery.ToolTip = toolTip;
		HeaderBatteryDots.ToolTip = toolTip;
	}

	private void ApplyBatteryHealthRatingFromDiagnostics(string? raw)
	{
		_batteryHealthRating = BatteryHealthRatingFromDiagnostics(raw);
		UpdateBatteryHealthDisplay();
		AddActivity("Battery", string.IsNullOrWhiteSpace(_batteryHealthRating) ? "Dell diagnostics log did not contain a battery health rating." : ("Dell diagnostics log battery rating loaded: " + _batteryHealthRating + "."));
	}

	private async Task ApplyStartupBatteryAsync(Task<string> task)
	{
		try
		{
			_batterySummary = await task;
			UpdateBatteryHealthDisplay();
			AddActivity("Battery", "Battery summary loaded: " + _batterySummary);
		}
		catch (Exception ex)
		{
			_batterySummary = "Battery Health: unavailable";
			UpdateBatteryHealthDisplay();
			AddActivity("Battery", "Battery health load failed: " + ex.Message);
		}
	}

	private async Task ApplyStartupCurrentBatteryAsync(Task<CurrentBatterySnapshot> task)
	{
		try
		{
			_currentBattery = await task;
			UpdateCurrentBatteryDisplay();
			AddActivity("Battery", _currentBattery.IsPresent ? $"Current battery loaded: {_currentBattery.Percent}% - {_currentBattery.Status}." : "Current battery status unavailable.");
		}
		catch (Exception ex)
		{
			_currentBattery = new CurrentBatterySnapshot
			{
				Status = "Unavailable"
			};
			UpdateCurrentBatteryDisplay();
			AddActivity("Battery", "Current battery status load failed: " + ex.Message);
		}
	}

	private void UpdateCurrentBatteryDisplay()
	{
		bool flag = _currentBattery.IsPresent && _currentBattery.Percent >= 0;
		int num = (flag ? Math.Clamp(_currentBattery.Percent, 0, 100) : 0);
		string value = (string.IsNullOrWhiteSpace(_currentBattery.Status) ? "Unavailable" : _currentBattery.Status.Trim());
		Brush brush = CurrentBatteryBrush(num, _currentBattery.IsCharging || _currentBattery.IsPluggedIn, flag);
		CurrentBatteryPercent.Text = (flag ? $"{num}%" : "--%");
		CurrentBatteryPanel.ToolTip = (flag ? $"Current battery: {num}% - {value}" : "Current battery status unavailable.");
		CurrentBatteryFill.Width = (flag ? Math.Max(2.0, Math.Round(19.0 * (double)num / 100.0)) : 0.0);
		CurrentBatteryFill.Fill = brush;
		Brush background = brush;
		CurrentBatteryShell.Background = background;
		CurrentBatteryCap.Background = background;
		CurrentBatteryChamber.Background = BrushFromHex((_currentTheme == "Light") ? "#D9E1E0" : ((_currentTheme == "AMOLED") ? "#1A1A1A" : "#53616A"));
		CurrentBatteryChargeBolt.Visibility = ((!_currentBattery.IsCharging && !_currentBattery.IsPluggedIn) ? Visibility.Collapsed : Visibility.Visible);
		CurrentBatteryChargeBolt.Fill = BrushFromHex((_currentTheme == "Light") ? "#12323A" : ((_currentTheme == "AMOLED") ? "#050505" : "#1B2730"));
	}

	private Brush CurrentBatteryBrush(int percent, bool powered, bool isPresent)
	{
		if (!isPresent)
		{
			return (Brush)FindResource("MutedBrush");
		}
		if (powered)
		{
			return BrushFromHex((_currentTheme == "AMOLED") ? "#DADADA" : "#22C55E");
		}
		if (percent <= 20)
		{
			return BrushFromHex((_currentTheme == "AMOLED") ? "#8A8A8A" : "#EF4444");
		}
		if (percent <= 40)
		{
			return BrushFromHex((_currentTheme == "AMOLED") ? "#B0B0B0" : "#F59E0B");
		}
		return BrushFromHex((_currentTheme == "Light") ? "#22C55E" : ((_currentTheme == "AMOLED") ? "#DADADA" : "#A2E6DD"));
	}

	private async Task ApplyStartupHardwareAsync(Task<HardwareSnapshot> task)
	{
		try
		{
			_hardware = await task;
			_hardware.Computer = PreferredQaComputerName(_hardware.Computer, _serviceTag, _hardware.BiosSerialNumber, _hardware.ChassisSerial);
			AddActivity("Hardware", $"Hardware snapshot loaded: {_hardware.Manufacturer} {_hardware.Model}; BIOS {_hardware.Bios}; Memory {_hardware.Memory}.");
		}
		catch (Exception ex)
		{
			_hardware = new HardwareSnapshot();
			AddActivity("Hardware", "Hardware snapshot load failed: " + ex.Message);
		}
	}

	private async Task ApplyStartupDiagnosticsAsync(Task<DiagnosticsResult> task)
	{
		try
		{
			DiagnosticsResult diagnosticsResult = await task;
			string text = RetainDiagnosticsLogOnSourceDrive(diagnosticsResult.Path);
			if (!string.IsNullOrWhiteSpace(text))
			{
				diagnosticsResult = diagnosticsResult with
				{
					Path = text
				};
			}
			_diagnosticsLogPath = diagnosticsResult.Path;
			_diagnosticsRawText = diagnosticsResult.RawText;
			ApplyBatteryHealthRatingFromDiagnostics(_diagnosticsRawText);
			DiagnosticsRawButton.IsEnabled = !string.IsNullOrWhiteSpace(_diagnosticsRawText);
			SetStep("Diagnostics", DiagnosticsIcon, DiagnosticsMain, DiagnosticsDetail, diagnosticsResult.State, diagnosticsResult.MainText, diagnosticsResult.DetailText);
			AddActivity("Diagnostics", string.IsNullOrWhiteSpace(diagnosticsResult.Path) ? "Dell preboot diagnostics log was not found." : ("Dell preboot diagnostics log loaded: " + diagnosticsResult.Path));
		}
		catch (Exception ex)
		{
			DiagnosticsRawButton.IsEnabled = false;
			SetStep("Diagnostics", DiagnosticsIcon, DiagnosticsMain, DiagnosticsDetail, "Bad", "Diagnostics log failed", ex.Message);
			AddActivity("Diagnostics", "Diagnostics log load failed: " + ex.Message);
		}
	}

	#endregion

	#region Live device monitoring, storage cleanup, and startup data collection

	private async void ExternalTestButton_Click(object sender, RoutedEventArgs e)
	{
		AddActivity("External Video", "Start selected; previous external-video result cleared before rerunning the test.");
		await RunExternalDisplayScanAsync(userInitiated: true);
	}

	private void StartExternalDisplayPolling()
	{
		_externalDisplayPollTimer?.Stop();
		_externalDisplayPollTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5L)
		};
		_externalDisplayPollTimer.Tick += async delegate
		{
			if (_qaLiveMonitoringActive && !_externalDisplayScanRunning)
			{
				await RunExternalDisplayScanAsync(userInitiated: false);
			}
		};
		_externalDisplayPollTimer.Start();
	}

	private void StartCurrentBatteryPolling()
	{
		_currentBatteryPollTimer?.Stop();
		_currentBatteryPollTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(15L)
		};
		_currentBatteryPollTimer.Tick += async delegate
		{
			await RefreshCurrentBatteryAsync();
		};
		_currentBatteryPollTimer.Start();
	}

	private async Task RefreshCurrentBatteryAsync()
	{
		if (_currentBatteryRefreshRunning)
		{
			return;
		}
		_currentBatteryRefreshRunning = true;
		try
		{
			int previousPercent = _currentBattery.Percent;
			string previousStatus = _currentBattery.Status;
			_currentBattery = await GetCurrentBatterySnapshotAsync();
			UpdateCurrentBatteryDisplay();
			if (_qaSessionReady)
			{
				SaveQaSessionCache();
			}
			if (_currentBattery.Percent != previousPercent || !string.Equals(_currentBattery.Status, previousStatus, StringComparison.Ordinal))
			{
				AddActivity("Battery", _currentBattery.IsPresent ? $"Current battery updated: {_currentBattery.Percent}% - {_currentBattery.Status}." : "Current battery status unavailable.");
			}
		}
		finally
		{
			_currentBatteryRefreshRunning = false;
		}
	}

	private async Task RunExternalDisplayScanAsync(bool userInitiated)
	{
		if (_externalDisplayScanRunning)
		{
			return;
		}
		_externalDisplayScanRunning = true;
		if (userInitiated)
		{
			ExternalTestButton.IsEnabled = false;
			SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Working", "Scanning for external display...", "Checking the active Windows display configuration.");
			AddActivity("External Video", "Manual external display scan started.");
		}
		try
		{
			ExternalDisplaySnapshot externalDisplaySnapshot = await GetExternalDisplaySnapshotAsync();
			string previousState = _states.GetValueOrDefault("ExternalVideo", "Waiting");
			string previousMain = ExternalMain.Text;
			string previousDetail = ExternalDetail.Text;
			if (externalDisplaySnapshot.HasExternalDisplay)
			{
				if (previousState != "Ok" || previousMain != "External display detected" || previousDetail != externalDisplaySnapshot.DetailText)
				{
					SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Ok", "External display detected", externalDisplaySnapshot.DetailText);
					AddActivity("External Video", (userInitiated ? "Manual" : "Automatic") + " scan detected an external display; " + externalDisplaySnapshot.DetailText);
				}
			}
			else if (previousState != "Ok" && previousState != "Bad" && previousState != "Ignored" &&
				(previousMain != "External display not detected yet" || previousDetail != "Monitoring remains active. Connect the display or use Fail to record a failed test."))
			{
				SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Waiting", "External display not detected yet", "Monitoring remains active. Connect the display or use Fail to record a failed test.");
				AddActivity("External Video", $"{(userInitiated ? "Manual" : "Automatic")} scan did not detect an external display; the test remains pending. Active displays = {externalDisplaySnapshot.ActiveDisplayCount}.");
			}
		}
		catch (Exception ex)
		{
			if (userInitiated)
			{
				SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Waiting", "External display scan unavailable", "The scan could not complete. Monitoring remains active; use Fail only after confirming the video output does not work. " + ex.Message);
				AddActivity("External Video", "Manual external display scan failed: " + ex.Message);
			}
		}
		finally
		{
			_externalDisplayScanRunning = false;
			if (userInitiated)
			{
				ExternalTestButton.IsEnabled = true;
			}
		}
	}

	private async Task<ExternalDisplaySnapshot> GetExternalDisplaySnapshotAsync()
	{
		ExternalDisplaySnapshot snapshot = await Task.Run((Func<ExternalDisplaySnapshot>)GetExternalDisplaySnapshot);
		if (snapshot.HasExternalDisplay)
		{
			return snapshot;
		}
		try
		{
			Dictionary<string, string> dictionary = JsonToDictionary(await PowerShellJsonAsync("$externalTechnologies = @(0, 1, 2, 3, 4, 5, 8, 9, 10, 12, 14, 15, 16, 17, 18)\n$connections = @(Get-CimInstance -Namespace root\\wmi -ClassName WmiMonitorConnectionParams -ErrorAction Stop | Where-Object { $_.Active })\n$externalConnections = @($connections | Where-Object { $externalTechnologies -contains [long]$_.VideoOutputTechnology })\n[pscustomobject]@{\n  Connected = ($externalConnections.Count -gt 0 -or $connections.Count -gt 1)\n  ActiveConnectionCount = $connections.Count\n  Technologies = (@($connections | ForEach-Object { [string]$_.VideoOutputTechnology } | Sort-Object -Unique) -join ', ')\n} | ConvertTo-Json -Compress"));
			if (bool.TryParse(dictionary.GetValueOrDefault("Connected", "False"), out var result) && result)
			{
				int num = ParseInt(dictionary.GetValueOrDefault("ActiveConnectionCount", "0"), 0);
				string valueOrDefault = dictionary.GetValueOrDefault("Technologies", "");
				string text = $"Physical external monitor connection detected by Windows ({num} active monitor connection{((num == 1) ? "" : "s")}).";
				if (!string.IsNullOrWhiteSpace(valueOrDefault))
				{
					text = text + " Output technology: " + valueOrDefault + ".";
				}
				return snapshot with
				{
					PhysicalExternalConnected = true,
					ConnectionDetail = text
				};
			}
		}
		catch (Exception ex)
		{
			AddActivity("External Video", "Physical monitor connection check was unavailable: " + ex.Message);
		}
		return snapshot;
	}

	private static ExternalDisplaySnapshot GetExternalDisplaySnapshot()
	{
		IReadOnlyList<ExternalDisplayInfo> activeDisplayInfos = GetActiveDisplayInfos();
		if (activeDisplayInfos.Count > 0)
		{
			return new ExternalDisplaySnapshot(activeDisplayInfos.Count, activeDisplayInfos);
		}
		return new ExternalDisplaySnapshot(Math.Max(1, CountActiveDisplays()), Array.Empty<ExternalDisplayInfo>());
	}

	private static IReadOnlyList<ExternalDisplayInfo> GetActiveDisplayInfos()
	{
		List<ExternalDisplayInfo> list = new List<ExternalDisplayInfo>();
		try
		{
			uint num = 0u;
			while (true)
			{
				DisplayDevice lpDisplayDevice = CreateDisplayDevice();
				if (!EnumDisplayDevices(null, num, ref lpDisplayDevice, 0u))
				{
					break;
				}
				if ((lpDisplayDevice.StateFlags & 1) != 0)
				{
					DevMode devMode = CreateDevMode();
					EnumDisplaySettings(lpDisplayDevice.DeviceName, -1, ref devMode);
					string text = "";
					for (uint num2 = 0u; num2 < 8; num2++)
					{
						DisplayDevice lpDisplayDevice2 = CreateDisplayDevice();
						if (!EnumDisplayDevices(lpDisplayDevice.DeviceName, num2, ref lpDisplayDevice2, 0u))
						{
							break;
						}
						string text2 = CleanDisplayText(lpDisplayDevice2.DeviceString);
						if (!string.IsNullOrWhiteSpace(text2) && (string.IsNullOrWhiteSpace(text) || text.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase)))
						{
							text = text2;
						}
					}
					string text3 = CleanDisplayText(lpDisplayDevice.DeviceName);
					if (string.IsNullOrWhiteSpace(text3))
					{
						text3 = $"Display {num + 1}";
					}
					list.Add(new ExternalDisplayInfo(text3, text, (int)devMode.dmPelsWidth, (int)devMode.dmPelsHeight, devMode.dmPositionX, devMode.dmPositionY, (lpDisplayDevice.StateFlags & 4) != 0));
				}
				num++;
			}
			return list;
		}
		catch
		{
			return Array.Empty<ExternalDisplayInfo>();
		}
	}

	private static DisplayDevice CreateDisplayDevice()
	{
		return new DisplayDevice
		{
			cb = Marshal.SizeOf<DisplayDevice>()
		};
	}

	private static DevMode CreateDevMode()
	{
		return new DevMode
		{
			dmSize = (ushort)Marshal.SizeOf<DevMode>()
		};
	}

	private static string CleanDisplayText(string? value)
	{
		value = value?.Trim() ?? "";
		if (!string.IsNullOrWhiteSpace(value))
		{
			return Regex.Replace(value, "\\s+", " ");
		}
		return "";
	}

	private static int CountActiveDisplays()
	{
		int count = 0;
		try
		{
			EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate
			{
				count++;
				return true;
			}, IntPtr.Zero);
		}
		catch
		{
			return 1;
		}
		return count;
	}

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc callback, nint dwData);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

	private string RetainDiagnosticsLogOnSourceDrive(string originalPath)
	{
		if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
		{
			return "";
		}
		try
		{
			string fullPath = Path.GetFullPath(originalPath);
			if (Path.GetFileName(fullPath).StartsWith("DellPrebootDiagnosticsLog-", StringComparison.OrdinalIgnoreCase))
			{
				AddActivity("Diagnostics", "Retained diagnostics log will be used from the FAT32 drive: " + fullPath);
				return fullPath;
			}
			string serial = SafeFile(_serviceTag, "unknown");
			DateTime now = DateTime.Now;
			string text2 = now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
			if (string.Equals(Path.GetFileName(fullPath), "DellPrebootDiagnosticsLog.txt", StringComparison.OrdinalIgnoreCase))
			{
				return ArchiveOriginalDiagnosticsLog(fullPath, serial, text2, now);
			}
			return fullPath;
		}
		catch (Exception ex)
		{
			AddActivity("Diagnostics", "Dell diagnostics log retention failed; the source log remains in place. " + ex.Message);
			return originalPath;
		}
	}

	private void UpdateDeviceNameHeader()
	{
		string deviceName = PreferredQaComputerName(_hardware.Computer, _serviceTag, _hardware.BiosSerialNumber, _hardware.ChassisSerial, _assetTag);
		HeaderDeviceName.Text = L("Device Name: " + SafeFile(deviceName, "Laptop"));
		HeaderDeviceName.ToolTip = "The device name is generated from the format saved in Config.";
	}

	private void SetSummaryStatus(string status)
	{
		SummaryStatus.Text = status;
		bool ready = string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "QA Complete", StringComparison.OrdinalIgnoreCase);
		SummaryStatusBubble.Background = BrushFromHex(ready ? "#D9F5E6" : "#FFF2C2");
		SummaryStatusBubble.BorderBrush = BrushFromHex(ready ? "#7CCBA0" : "#F2D36B");
		SummaryStatus.Foreground = BrushFromHex(ready ? "#12633D" : "#6B4D00");
	}

	private void BeginProcessing(string operation)
	{
		_processingOperations.Add(operation);
		SetSummaryStatus("Processing");
	}

	private void EndProcessing(string operation)
	{
		if (!_processingOperations.Remove(operation) || _processingOperations.Count > 0 || string.Equals(SummaryStatus.Text, "Closing", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		SetSummaryStatus(_qaSessionReady && _completionCelebrated && IsQaComplete() ? "QA Complete" : "Ready");
	}

	private void MainWindow_Closing(object? sender, CancelEventArgs e)
	{
		_headerClockTimer?.Stop();
		_headerClockTimer = null;
		_qaSessionSaveTimer.Stop();
		_qaLiveMonitoringActive = false;
		_externalDisplayPollTimer?.Stop();
		_externalDisplayPollTimer = null;
		_currentBatteryPollTimer?.Stop();
		_currentBatteryPollTimer = null;
		_usbPortPollTimer?.Stop();
		_usbPortPollTimer = null;
		_usbDeviceChangeDebounceTimer?.Stop();
		_usbDeviceChangeDebounceTimer = null;
		_windowSource?.RemoveHook(UsbDeviceWindowProc);
		_windowSource = null;
		if (!_closeCleanupComplete)
		{
			_closeCleanupComplete = true;
			RunExitCleanup("close");
		}
	}

	private void RunExitCleanup(string reason)
	{
		SetSummaryStatus("Closing");
		base.Dispatcher.Invoke(delegate
		{
		}, DispatcherPriority.Render);
		AddActivity("Cleanup", "Exit cleanup started before " + reason + ".");
		SaveQaSessionCache();
		CleanupLocalFilesBeforeClose();
		SaveQaSessionCache();
	}

	private string ArchiveOriginalDiagnosticsLog(string originalPath, string serial, string timestamp, DateTime archiveTime)
	{
		try
		{
			if (!File.Exists(originalPath))
			{
				return originalPath;
			}
			string? directoryName = Path.GetDirectoryName(originalPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				string text = Path.Combine(directoryName, $"DellPrebootDiagnosticsLog-{serial}-{timestamp}.txt");
				int num = 1;
				while (File.Exists(text))
				{
					text = Path.Combine(directoryName, $"DellPrebootDiagnosticsLog-{serial}-{timestamp}-{num++}.txt");
				}
				File.Move(originalPath, text);
				File.SetLastWriteTime(text, archiveTime);
				AddActivity("Diagnostics", "Original Dell diagnostics log retained as: " + text);
				CleanupDiagnosticsArchivesInFolder(directoryName);
				return text;
			}
		}
		catch (Exception ex)
		{
			AddActivity("Diagnostics", "Original Dell diagnostics log could not be renamed; it was retained at " + originalPath + ". " + ex.Message);
		}
		return originalPath;
	}

	private void CleanupDiagnosticsSourceArchives()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string text = _config.DellDiagnosticsLogFolder?.Trim() ?? "";
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			hashSet.Add(Path.GetFullPath(text));
		}
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				try
				{
					if (driveInfo.IsReady && driveInfo.DriveType == DriveType.Removable && string.Equals(driveInfo.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) && driveInfo.TotalSize > 0 && driveInfo.TotalSize <= 134217728)
					{
						hashSet.Add(driveInfo.RootDirectory.FullName);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		foreach (string item in hashSet)
		{
			CleanupDiagnosticsArchivesInFolder(item);
		}
	}

	private void CleanupDiagnosticsArchivesInFolder(string folder)
	{
		if (!Directory.Exists(folder))
		{
			return;
		}
		DateTime dateTime = DateTime.Now.AddDays(-90.0);
		string[] files = Directory.GetFiles(folder, "DellPrebootDiagnosticsLog-*.txt", SearchOption.TopDirectoryOnly);
		foreach (string text in files)
		{
			try
			{
				if (!(File.GetLastWriteTime(text) >= dateTime))
				{
					File.Delete(text);
					AddActivity("Diagnostics", $"Removed source diagnostics archive older than {90} days: {text}");
				}
			}
			catch (Exception ex)
			{
				AddActivity("Diagnostics", "Could not remove old source diagnostics archive " + Path.GetFileName(text) + ": " + ex.Message);
			}
		}
	}

	private void CleanupLocalFilesBeforeClose()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Laptop QA");
			string path = Path.Combine(text, "");
			string currentLocalStageRoot = GetCurrentLocalStageRoot(text);
			if (!string.IsNullOrWhiteSpace(currentLocalStageRoot))
			{
				RemoveLocalDataItems(Path.Combine(currentLocalStageRoot, "LAPTOP QA"));
			}
			CleanupAllLocalStageRoots(text, currentLocalStageRoot);
			TryDeleteDirectoryIfEmpty(path, "empty local version folder");
			TryDeleteEmptyChildDirectories(text, "empty local version folder");
			TryDeleteDirectoryIfEmpty(text, "empty local Laptop QA folder");
			if (string.IsNullOrWhiteSpace(currentLocalStageRoot))
			{
				AddActivity("Cleanup", "Local staged file cleanup completed. App is not running from the local staging folder.");
			}
			else
			{
				AddActivity("Cleanup", "Local staged file cleanup completed. Active app folder will be removed by the launcher after close: " + currentLocalStageRoot);
			}
		}
		catch (Exception ex)
		{
			AddActivity("Cleanup", "Local staged file cleanup failed: " + ex.Message);
		}
	}

	private string GetCurrentLocalStageRoot(string localBase)
	{
		try
		{
			string text = Path.GetFullPath(_appRoot).TrimEnd(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			});
			string value = Path.GetFullPath(localBase).TrimEnd(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			}) + Path.DirectorySeparatorChar;
			if (!text.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			DirectoryInfo directoryInfo = new DirectoryInfo(text);
			if (!string.Equals(directoryInfo.Name, "LAPTOP QA", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return directoryInfo.Parent?.FullName ?? "";
		}
		catch
		{
			return "";
		}
	}

	private void CleanupAllLocalStageRoots(string localBase, string currentLocalStageRoot)
	{
		if (!Directory.Exists(localBase))
		{
			return;
		}
		foreach (string item in Directory.EnumerateDirectories(localBase))
		{
			CleanupOldLocalStageRoots(item, currentLocalStageRoot);
			TryDeleteDirectoryIfEmpty(item, "empty local version folder");
		}
	}

	private void CleanupOldLocalStageRoots(string versionRoot, string currentLocalStageRoot)
	{
		if (!Directory.Exists(versionRoot))
		{
			return;
		}
		string text = (string.IsNullOrWhiteSpace(currentLocalStageRoot) ? "" : Path.GetFullPath(currentLocalStageRoot).TrimEnd(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		}));
		foreach (string item in Directory.EnumerateDirectories(versionRoot))
		{
			string text2 = Path.GetFullPath(item).TrimEnd(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			});
			if (string.IsNullOrWhiteSpace(text) || !string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
			{
				TryDeleteDirectory(text2, "old local staged app folder");
			}
		}
	}

	private void RemoveLocalDataItems(string localApp)
	{
		string[] array = new string[6] { ".runtime", "hardware", "hash", "logs", "QA sheets", "Laptop-QA-Config.json" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(localApp, path);
			if (Directory.Exists(path2))
			{
				TryDeleteDirectory(path2, "local data folder");
			}
			else if (File.Exists(path2))
			{
				TryDeleteFile(path2, "local data file");
			}
		}
	}

	private void TryDeleteDirectory(string path, string label)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
				AddActivity("Cleanup", "Deleted " + label + ": " + path);
			}
		}
		catch (Exception ex)
		{
			AddActivity("Cleanup", $"Could not delete {label}: {path}. {ex.Message}");
		}
	}

	private void TryDeleteDirectoryIfEmpty(string path, string label)
	{
		try
		{
			if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
			{
				Directory.Delete(path, recursive: false);
				AddActivity("Cleanup", "Deleted " + label + ": " + path);
			}
		}
		catch (Exception ex)
		{
			AddActivity("Cleanup", $"Could not delete {label}: {path}. {ex.Message}");
		}
	}

	private void TryDeleteEmptyChildDirectories(string path, string label)
	{
		try
		{
			if (!Directory.Exists(path))
			{
				return;
			}
			foreach (string item in Directory.EnumerateDirectories(path))
			{
				TryDeleteDirectoryIfEmpty(item, label);
			}
		}
		catch (Exception ex)
		{
			AddActivity("Cleanup", "Could not scan local cleanup folder: " + path + ". " + ex.Message);
		}
	}

	private void TryDeleteFile(string path, string label)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
				AddActivity("Cleanup", "Deleted " + label + ": " + path);
			}
		}
		catch (Exception ex)
		{
			AddActivity("Cleanup", $"Could not delete {label}: {path}. {ex.Message}");
		}
	}

	private async Task AwaitStartupStepAsync(string section, Task task)
	{
		try
		{
			await task;
		}
		catch (Exception ex)
		{
			AddActivity(section, section + " startup step failed: " + ex.Message);
		}
	}

	private async Task SetStartupSplashStatusAsync(string? message)
	{
		StartupSplashStatus.Text = (string.IsNullOrWhiteSpace(message) ? "Loading..." : message);
		await base.Dispatcher.InvokeAsync(delegate
		{
		}, DispatcherPriority.Render);
	}

	private void BringStartupSplashToFront()
	{
		if (StartupSplashOverlay.Visibility == Visibility.Visible)
		{
			if (base.WindowState == WindowState.Minimized)
			{
				base.WindowState = WindowState.Normal;
			}
			base.Topmost = true;
			Activate();
			Focus();
		}
	}

	private async Task HideStartupSplashAsync()
	{
		if (StartupSplashOverlay.Visibility != Visibility.Visible)
		{
			base.Topmost = false;
			return;
		}
		await base.Dispatcher.InvokeAsync(delegate
		{
		}, DispatcherPriority.Render);
		await Task.Delay(180);
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180L))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		doubleAnimation.Completed += delegate
		{
			StartupSplashOverlay.Visibility = Visibility.Collapsed;
			StartupSplashOverlay.Opacity = 1.0;
			base.Topmost = false;
			Activate();
		};
		StartupSplashOverlay.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		await Task.Delay(240);
		base.Topmost = false;
	}

	private async Task<Dictionary<string, string>> GetHeaderAsync()
	{
		return JsonToDictionary(await PowerShellJsonAsync("$bios = Get-CimInstance Win32_BIOS -ErrorAction Stop\n$enc = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue | Select-Object -First 1\n[pscustomobject]@{\n  Serial = [string]$bios.SerialNumber\n  Asset = [string]$enc.SMBIOSAssetTag\n} | ConvertTo-Json -Compress"));
	}

	private async Task RefreshWarrantyAsync()
	{
		await RefreshWarrantyComparisonDateAsync();
		if (string.IsNullOrWhiteSpace(_serviceTag) || _serviceTag.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
		{
			_warranty = "";
			HeaderWarranty.Text = L("Warranty: X unavailable");
			HeaderWarranty.ToolTip = "Service Tag unavailable.";
			AddActivity("Warranty", "Warranty lookup skipped: Service Tag unavailable.");
			return;
		}
		if (!string.IsNullOrWhiteSpace(_warranty) && string.Equals(_warrantyCachedServiceTag, _serviceTag, StringComparison.OrdinalIgnoreCase))
		{
			HeaderWarranty.Text = L("Warranty: " + WarrantyDisplayText());
			HeaderWarranty.ToolTip = WarrantyToolTipText();
			AddActivity("Warranty", "Using cached warranty expiration: " + _warranty);
			return;
		}
		HeaderWarranty.Text = L("Warranty: loading...");
		HeaderWarranty.ToolTip = "Warranty lookup is running.";
		AddActivity("Warranty", "Warranty lookup started.");
		WarrantyResult warrantyResult = await GetDellWarrantyExpirationAsync(_serviceTag);
		if (warrantyResult.Found && !string.IsNullOrWhiteSpace(warrantyResult.ExpirationDateText))
		{
			_warranty = warrantyResult.ExpirationDateText;
			_warrantyCachedServiceTag = _serviceTag;
			HeaderWarranty.Text = L("Warranty: " + WarrantyDisplayText());
			HeaderWarranty.ToolTip = WarrantyToolTipText();
			AddActivity("Warranty", "Warranty expiration loaded: " + _warranty);
		}
		else
		{
			_warranty = "";
			HeaderWarranty.Text = L("Warranty: X unavailable");
			HeaderWarranty.ToolTip = (string.IsNullOrWhiteSpace(warrantyResult.Message) ? "No warranty expiration date returned." : warrantyResult.Message);
			AddActivity("Warranty", $"Warranty expiration not loaded: {HeaderWarranty.ToolTip}");
		}
	}

	private string WarrantyDisplayText()
	{
		return WarrantyDisplayText(_warranty);
	}

	private string WarrantyDisplayText(string? warrantyText)
	{
		if (string.IsNullOrWhiteSpace(warrantyText))
		{
			return "unavailable X";
		}
		string trimmed = warrantyText.Trim();
		return trimmed + (IsWarrantyCurrent(trimmed) ? " \u2713" : " X");
	}

	private string WarrantyToolTipText()
	{
		if (string.IsNullOrWhiteSpace(_warranty))
		{
			return "Warranty was not loaded.";
		}
		return IsWarrantyCurrent(_warranty)
			? "Warranty is active through " + _warranty + ". Checked against " + _warrantyComparisonDateSource + ": " + _warrantyComparisonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: "Warranty expired on " + _warranty + ". Checked against " + _warrantyComparisonDateSource + ": " + _warrantyComparisonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	private bool IsWarrantyCurrent(string warrantyText)
	{
		DateTime? date = ParseWarrantyCliDate(warrantyText);
		return date.HasValue && date.Value.Date >= _warrantyComparisonDate.Date;
	}

	private async Task RefreshWarrantyComparisonDateAsync()
	{
		try
		{
			DateTime? networkDate = await GetNetworkDateAsync();
			if (networkDate.HasValue)
			{
				_warrantyComparisonDate = networkDate.Value.Date;
				_warrantyComparisonDateSource = "network date";
				AddActivity("Warranty", "Warranty date comparison will use network date: " + _warrantyComparisonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
				return;
			}
		}
		catch (Exception ex)
		{
			AddActivity("Warranty", "Network date lookup failed: " + ex.Message);
		}

		_warrantyComparisonDate = DateTime.Today;
		_warrantyComparisonDateSource = "Windows system clock";
		AddActivity("Warranty", "Warranty date comparison will use Windows system clock: " + _warrantyComparisonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
	}

	private static async Task<DateTime?> GetNetworkDateAsync()
	{
		string[] urls =
		{
			"https://www.microsoft.com",
			"https://www.bing.com",
			"https://www.dell.com"
		};
		using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(4.0) };
		foreach (string requestUri in urls)
		{
			try
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, requestUri);
				using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
				DateTimeOffset? date = response.Headers.Date;
				if (date.HasValue)
				{
					return date.Value.LocalDateTime.Date;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private async Task<WarrantyResult> GetDellWarrantyExpirationAsync(string serviceTag)
	{
		if (string.IsNullOrWhiteSpace(serviceTag))
		{
			return WarrantyResult.NotFound("Service Tag unavailable.");
		}
		try
		{
			string workDir = Path.Combine(RuntimeDir, "warranty");
			Directory.CreateDirectory(workDir);
			string text = TryGetCachedWarrantyExpiration(workDir, serviceTag);
			if (!string.IsNullOrWhiteSpace(text))
			{
				_warrantyWaitingForNetwork = false;
				AddActivity("Warranty", "Warranty expiration loaded from the local Dell CLI cache: " + text);
				return new WarrantyResult(Found: true, text, "");
			}
			if (!HasUsableNetworkConnection())
			{
				_warrantyWaitingForNetwork = true;
				return WarrantyResult.NotFound("Warranty was not found in the local CLI cache. Waiting for Network Check before the online lookup.");
			}
			_warrantyWaitingForNetwork = false;
			string cliPath = ResolveDellWarrantyCliPath();
			if (string.IsNullOrWhiteSpace(cliPath))
			{
				return WarrantyResult.NotFound("Dell Command | Warranty CLI was not found.");
			}
			string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
			string inputPath = Path.Combine(workDir, "service-tag-" + runId + ".csv");
			await File.WriteAllTextAsync(inputPath, serviceTag.Trim());
			AddActivity("Warranty", "Starting Dell's warranty CLI; the CLI will perform the authoritative online lookup.");
			List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
			string lastFailure = "Dell Command | Warranty returned no warranty records.";
			for (int attempt = 1; attempt <= 2; attempt++)
			{
				string outputPath = Path.Combine(workDir, $"warranty-output-{runId}-attempt{attempt}.csv");
				try
				{
					AddActivity("Warranty", $"Running Dell Command | Warranty CLI (attempt {attempt} of {2}).");
					string text2 = await RunProcessCaptureAsync(cliPath, new string[3]
					{
						"/I=" + inputPath,
						"/E=" + outputPath,
						"/V"
					}, 60);
					if (!string.IsNullOrWhiteSpace(text2))
					{
						AddActivity("Warranty", "Dell Command | Warranty CLI completed successfully.");
					}
					if (!File.Exists(outputPath))
					{
						lastFailure = "Dell Command | Warranty did not create an output CSV.";
						goto IL_03f1;
					}
					rows = ReadWarrantyCsv(outputPath);
					if (rows.Count > 0)
					{
						break;
					}
					lastFailure = "Dell Command | Warranty returned no warranty records.";
					if (!string.IsNullOrWhiteSpace(text2))
					{
						AddActivity("Warranty", "Dell CLI details: " + RedactServiceTag(text2.Trim(), serviceTag));
					}
					goto IL_03f1;
				}
				catch (Exception ex)
				{
					lastFailure = RedactServiceTag(ex.Message, serviceTag);
					goto IL_03f1;
				}
				IL_03f1:
				if (attempt < 2)
				{
					AddActivity("Warranty", lastFailure + " Making one follow-up lookup shortly.");
					await Task.Delay(TimeSpan.FromSeconds(5L));
				}
			}
			if (rows.Count == 0)
			{
				return WarrantyResult.NotFound(lastFailure);
			}
			List<DateTime> list = (from date in rows.Select((Dictionary<string, string> row) => TryGetFirstValue(row, "End Date", "EndDate", "Warranty End Date", "Expiration Date")).Select(ParseWarrantyCliDate)
				where date.HasValue
				select date.Value).ToList();
			if (list.Count == 0)
			{
				return WarrantyResult.NotFound("Dell Command | Warranty returned records without an end date.");
			}
			return new WarrantyResult(Found: true, list.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "");
		}
		catch (Exception ex2)
		{
			return WarrantyResult.NotFound(RedactServiceTag(ex2.Message, serviceTag));
		}
	}

	private string ResolveDellWarrantyCliPath()
	{
		string text = _config.DellWarrantyCliPath.Trim();
		return new string[4]
		{
			text,
			Path.Combine(_appRoot, "tools", "DellWarranty", "DellWarranty-CLI.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Dell", "CommandIntegrationSuite", "DellWarranty-CLI.exe"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dell", "CommandIntegrationSuite", "DellWarranty-CLI.exe")
		}.FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? "";
	}

	private static string TryGetCachedWarrantyExpiration(string workDir, string serviceTag)
	{
		try
		{
			List<DateTime> list = (from date in (from row in Directory.EnumerateFiles(workDir, "warranty*.csv").OrderByDescending(File.GetLastWriteTimeUtc).SelectMany(ReadWarrantyCsv)
					where string.Equals(TryGetFirstValue(row, "Service Tag", "ServiceTag"), serviceTag.Trim(), StringComparison.OrdinalIgnoreCase)
					select TryGetFirstValue(row, "End Date", "EndDate", "Warranty End Date", "Expiration Date")).Select(ParseWarrantyCliDate)
				where date.HasValue
				select date.Value).ToList();
			return (list.Count == 0) ? "" : list.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}
		catch
		{
			return "";
		}
	}

	private static bool HasUsableNetworkConnection()
	{
		try
		{
			return NetworkInterface.GetAllNetworkInterfaces().Any(delegate(NetworkInterface adapter)
			{
				if (adapter.OperationalStatus == OperationalStatus.Up)
				{
					NetworkInterfaceType networkInterfaceType = adapter.NetworkInterfaceType;
					if (networkInterfaceType != NetworkInterfaceType.Loopback && networkInterfaceType != NetworkInterfaceType.Tunnel)
					{
						return adapter.GetIPProperties().UnicastAddresses.Any((UnicastIPAddressInformation address) => IsUsableAddress(address.Address));
					}
				}
				return false;
			});
		}
		catch
		{
			return false;
		}
	}

	private static bool IsUsableAddress(IPAddress address)
	{
		if (IPAddress.IsLoopback(address))
		{
			return false;
		}
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			byte[] addressBytes = address.GetAddressBytes();
			if (addressBytes[0] != 0 && addressBytes[0] != 127)
			{
				if (addressBytes[0] == 169)
				{
					return addressBytes[1] != 254;
				}
				return true;
			}
			return false;
		}
		if (address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal)
		{
			return !address.IsIPv6Multicast;
		}
		return false;
	}

	private static List<Dictionary<string, string>> ReadWarrantyCsv(string path)
	{
		List<string> list = (from line in File.ReadAllLines(path)
			where !string.IsNullOrWhiteSpace(line)
			select line).ToList();
		if (list.Count < 2)
		{
			return new List<Dictionary<string, string>>();
		}
		List<string> list2 = ParseCsvLine(list[0]);
		List<Dictionary<string, string>> list3 = new List<Dictionary<string, string>>();
		foreach (string item in list.Skip(1))
		{
			List<string> list4 = ParseCsvLine(item);
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int num = 0; num < list2.Count && num < list4.Count; num++)
			{
				dictionary[list2[num]] = list4[num];
			}
			list3.Add(dictionary);
		}
		return list3;
	}

	private static List<string> ParseCsvLine(string line)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			switch (c)
			{
			case '"':
				if (flag && i + 1 < line.Length && line[i + 1] == '"')
				{
					stringBuilder.Append('"');
					i++;
				}
				else
				{
					flag = !flag;
				}
				continue;
			case ',':
				if (!flag)
				{
					list.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					continue;
				}
				break;
			}
			stringBuilder.Append(c);
		}
		list.Add(stringBuilder.ToString());
		return list;
	}

	private static string TryGetValue(Dictionary<string, string> row, string key)
	{
		if (!row.TryGetValue(key, out string? value))
		{
			return "";
		}
		return value;
	}

	private static string TryGetFirstValue(Dictionary<string, string> row, params string[] keys)
	{
		foreach (string key in keys)
		{
			if (row.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}
		return "";
	}

	private static int ParseInt(string value, int fallback)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static DateTime? ParseWarrantyCliDate(string text)
	{
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var result))
		{
			return result;
		}
		if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out result))
		{
			return null;
		}
		return result;
	}

	private async Task<string> GetBatterySummaryAsync()
	{
		try
		{
			return JsonToDictionary(await PowerShellJsonAsync("function ToInt($value) {\n  if ($null -eq $value) { return $null }\n  $text = ([string]$value) -replace '[^\\d-]', ''\n  if ([string]::IsNullOrWhiteSpace($text) -or $text -eq '-') { return $null }\n  try { return [int64]$text } catch { return $null }\n}\nfunction HealthLabel($percent) {\n  if ($percent -ge 65) { return 'Excellent' }\n  if ($percent -ge 51) { return 'Good' }\n  if ($percent -ge 26) { return 'Fair' }\n  return 'Poor'\n}\n$summary = 'Battery Health: unavailable'\n$reportPath = Join-Path $env:TEMP (\"laptop-qa-battery-{0}.xml\" -f ([guid]::NewGuid().ToString('N')))\ntry {\n  $powercfg = Join-Path $env:SystemRoot 'System32\\powercfg.exe'\n  if (Test-Path -LiteralPath $powercfg) {\n    & $powercfg /batteryreport /output $reportPath /xml | Out-Null\n    if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $reportPath)) {\n      [xml]$report = Get-Content -Raw -LiteralPath $reportPath\n      $ns = New-Object System.Xml.XmlNamespaceManager($report.NameTable)\n      $ns.AddNamespace('br', 'http://schemas.microsoft.com/battery/2012')\n      $batteryNodes = @($report.SelectNodes('//br:Batteries/br:Battery', $ns))\n      if ($batteryNodes.Count -eq 0) { $batteryNodes = @($report.SelectNodes('//*[local-name()=\"Batteries\"]/*[local-name()=\"Battery\"]')) }\n      $design = 0\n      $full = 0\n      foreach ($battery in $batteryNodes) {\n        $designNode = $battery.SelectSingleNode('br:DesignCapacity', $ns)\n        if ($null -eq $designNode) { $designNode = $battery.SelectSingleNode('*[local-name()=\"DesignCapacity\"]') }\n        $fullNode = $battery.SelectSingleNode('br:FullChargeCapacity', $ns)\n        if ($null -eq $fullNode) { $fullNode = $battery.SelectSingleNode('br:FullChargedCapacity', $ns) }\n        if ($null -eq $fullNode) { $fullNode = $battery.SelectSingleNode('*[local-name()=\"FullChargeCapacity\" or local-name()=\"FullChargedCapacity\"]') }\n        $d = ToInt $designNode.InnerText\n        $f = ToInt $fullNode.InnerText\n        if ($null -ne $d -and $d -gt 0 -and $null -ne $f -and $f -gt 0) {\n          $design += $d\n          $full += $f\n        }\n      }\n      if ($design -gt 0 -and $full -gt 0) {\n        $percent = [int][math]::Round(($full / $design) * 100)\n        if ($percent -gt 100) { $percent = 100 }\n        if ($percent -lt 0) { $percent = 0 }\n        $summary = \"Battery Health: $(HealthLabel $percent) ($percent%)\"\n      }\n    }\n  }\n} catch {\n} finally {\n  try { if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath -Force } } catch {}\n}\nif ($summary -eq 'Battery Health: unavailable') {\n  $b = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1\n  if ($null -ne $b -and $null -ne $b.EstimatedChargeRemaining) { $summary = \"Battery Health: $($b.EstimatedChargeRemaining)% reported\" }\n}\n[pscustomobject]@{ Summary = $summary } | ConvertTo-Json -Compress")).GetValueOrDefault("Summary", "Battery Health: unavailable");
		}
		catch
		{
			return "Battery Health: unavailable";
		}
	}

	private Task<CurrentBatterySnapshot> GetCurrentBatterySnapshotAsync()
	{
		return Task.Run(delegate
		{
			if (!GetSystemPowerStatus(out var status))
			{
				return new CurrentBatterySnapshot
				{
					Status = "Unavailable"
				};
			}
			bool num = (status.BatteryFlag & 0x80) != 0;
			int num2 = ((status.BatteryLifePercent == byte.MaxValue) ? (-1) : Math.Clamp((int)status.BatteryLifePercent, 0, 100));
			bool flag = !num && num2 >= 0;
			bool flag2 = status.ACLineStatus == 1;
			bool flag3 = flag && (status.BatteryFlag & 8) != 0;
			string status2 = "Unavailable";
			if (flag)
			{
				status2 = (flag3 ? "Charging" : ((flag2 && num2 >= 95) ? "Plugged in, full" : (flag2 ? "Plugged in" : (((status.BatteryFlag & 4) != 0) ? "Critical" : (((status.BatteryFlag & 2) != 0) ? "Low" : "On battery")))));
			}
			return new CurrentBatterySnapshot
			{
				IsPresent = flag,
				Percent = num2,
				Status = status2,
				IsCharging = flag3,
				IsPluggedIn = flag2
			};
		});
	}

	private async Task<DiagnosticsResult> GetDiagnosticsResultAsync()
	{
		string text = await Task.Run((Func<string>)FindDiagnosticsLogPath);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return await GetDiagnosticsResultFromPathAsync(text);
		}
		PreviousDiagnosticsMatch? previousDiagnosticsMatch = FindPreviousDiagnosticsMatch();
		if (previousDiagnosticsMatch is not null)
		{
			AddActivity("Diagnostics", "No new FAT32 diagnostics log was found. Previous filename match found for " + _serviceTag + ": " + previousDiagnosticsMatch.Path);
			if (ShowPreviousDiagnosticsMatchDialog(previousDiagnosticsMatch))
			{
				AddActivity("Diagnostics", "Previous diagnostics log confirmed for " + _serviceTag + ": " + previousDiagnosticsMatch.Path);
				return await GetDiagnosticsResultFromPathAsync(previousDiagnosticsMatch.Path);
			}
			AddActivity("Diagnostics", "Previous diagnostics log canceled for " + _serviceTag + ": " + previousDiagnosticsMatch.Path);
		}
		return new DiagnosticsResult("Warning", "Diagnostics log not found", (previousDiagnosticsMatch is null) ? "DellPrebootDiagnosticsLog.txt was not found on the small FAT32 diagnostics drive, and no retained filename matched this service tag." : "No new diagnostics log was found. A retained match was found, but the technician canceled it.", "", "");
	}

	private PreviousDiagnosticsMatch? FindPreviousDiagnosticsMatch()
	{
		string text = SafeFile(_serviceTag, "").Trim();
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Unknown", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				try
				{
					if (!driveInfo.IsReady || driveInfo.DriveType != DriveType.Removable || !string.Equals(driveInfo.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) || driveInfo.TotalSize <= 0 || driveInfo.TotalSize > 134217728)
					{
						continue;
					}
					foreach (string item2 in Directory.EnumerateFiles(driveInfo.RootDirectory.FullName, "DellPrebootDiagnosticsLog-*.txt", SearchOption.TopDirectoryOnly))
					{
						hashSet.Add(Path.GetFullPath(item2));
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		Regex serialPattern = new Regex("(?:^|[-_])" + Regex.Escape(text) + "(?:[-_]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		return (from PreviousDiagnosticsMatch match in from path in hashSet
				select TryCreatePreviousDiagnosticsMatch(path, serialPattern) into match
				where (object)match != null
				select match
			orderby match.Timestamp descending
			select match).FirstOrDefault();
	}

	private PreviousDiagnosticsMatch? TryCreatePreviousDiagnosticsMatch(string path, Regex serialPattern)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
			if (!serialPattern.IsMatch(fileNameWithoutExtension))
			{
				return null;
			}
			Match match = Regex.Match(fileNameWithoutExtension, "(?<!\\d)(?<date>\\d{8})-(?<time>\\d{6})(?:-(?<milliseconds>\\d{3}))?(?!\\d)", RegexOptions.CultureInvariant);
			if (!match.Success)
			{
				return null;
			}
			if (!DateTime.TryParseExact(match.Groups["date"].Value + match.Groups["time"].Value + (match.Groups["milliseconds"].Success ? match.Groups["milliseconds"].Value : "000"), "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			{
				return null;
			}
			return new PreviousDiagnosticsMatch(path, _serviceTag.Trim(), result);
		}
		catch
		{
			return null;
		}
	}

	private bool ShowPreviousDiagnosticsMatchDialog(PreviousDiagnosticsMatch match)
	{
		Window dialog = new Window
		{
			Title = "Previous Diagnostics Log Found",
			Width = 500.0,
			SizeToContent = SizeToContent.Height,
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ShowInTaskbar = false,
			Topmost = base.Topmost
		};
		Border border = new Border
		{
			Margin = new Thickness(18.0),
			Padding = new Thickness(26.0, 24.0, 26.0, 22.0),
			CornerRadius = new CornerRadius(18.0),
			Background = (Brush)FindResource("GlassPanelBrush"),
			BorderBrush = (Brush)FindResource("PanelStroke"),
			BorderThickness = new Thickness(1.0),
			Effect = new DropShadowEffect
			{
				BlurRadius = 24.0,
				ShadowDepth = 6.0,
				Opacity = 0.28,
				Color = Colors.Black
			}
		};
		StackPanel stackPanel = (StackPanel)(border.Child = new StackPanel());
		dialog.Content = border;
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Previous diagnostics log found",
			Foreground = (Brush)FindResource("TextBrush"),
			FontSize = 21.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "No new diagnostics log was found on the FAT32 diagnostics drive, but a retained log filename matches this laptop.",
			Foreground = (Brush)FindResource("MutedBrush"),
			FontSize = 13.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 18.0)
		});
		Border border2 = new Border
		{
			Padding = new Thickness(16.0, 13.0, 16.0, 13.0),
			CornerRadius = new CornerRadius(12.0),
			Background = (Brush)FindResource("NoteInputBrush"),
			BorderBrush = (Brush)FindResource("PanelStroke"),
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		border2.Child = new TextBlock
		{
			Text = $"Serial: {match.Serial}\nLog created: {match.Timestamp:MMMM d, yyyy 'at' h:mm:ss tt}",
			Foreground = (Brush)FindResource("TextBrush"),
			FontSize = 13.5,
			FontWeight = FontWeights.SemiBold,
			LineHeight = 23.0
		};
		stackPanel.Children.Add(border2);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Use this retained diagnostics log for the current QA?",
			Foreground = (Brush)FindResource("TextBrush"),
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button button = new Button
		{
			Content = "Cancel",
			Width = 92.0,
			Height = 34.0,
			Foreground = Brushes.White,
			Background = (Brush)FindResource("ResetButtonBrush"),
			BorderBrush = (Brush)FindResource("PanelStroke"),
			BorderThickness = new Thickness(1.0),
			FontWeight = FontWeights.SemiBold,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Template = ButtonChrome.RoundedTemplate(),
			IsCancel = true,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		Button button2 = new Button
		{
			Content = "Confirm",
			Width = 100.0,
			Height = 34.0,
			Foreground = Brushes.White,
			Background = (Brush)FindResource("PrimaryButtonBrush"),
			BorderThickness = new Thickness(0.0),
			FontWeight = FontWeights.SemiBold,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Template = ButtonChrome.RoundedTemplate(),
			IsDefault = true
		};
		button.Click += delegate
		{
			dialog.DialogResult = false;
			dialog.Close();
		};
		button2.Click += delegate
		{
			dialog.DialogResult = true;
			dialog.Close();
		};
		stackPanel2.Children.Add(button);
		stackPanel2.Children.Add(button2);
		stackPanel.Children.Add(stackPanel2);
		return dialog.ShowDialog() == true;
	}

	private static Task<DiagnosticsResult> GetDiagnosticsResultFromPathAsync(string path)
	{
		return Task.Run(delegate
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return new DiagnosticsResult("Bad", "Diagnostics log not found", "The selected diagnostics log file was not found.", "", "");
			}
			string raw = File.ReadAllText(path);
			return ParseDiagnosticsLog(path, raw);
		});
	}

	private string FindDiagnosticsLogPath()
	{
		try
		{
			return (from d in DriveInfo.GetDrives().Where(delegate(DriveInfo d)
				{
					try
					{
						return d.IsReady && d.DriveType == DriveType.Removable && string.Equals(d.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) && d.TotalSize > 0 && d.TotalSize <= 134217728 && File.Exists(Path.Combine(d.RootDirectory.FullName, "DellPrebootDiagnosticsLog.txt"));
					}
					catch
					{
						return false;
					}
				})
				orderby Math.Abs(d.TotalSize - 52428800)
				select Path.Combine(d.RootDirectory.FullName, "DellPrebootDiagnosticsLog.txt")).FirstOrDefault() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private string FindDiagnosticsBrowseStartFolder()
	{
		try
		{
			return (from d in DriveInfo.GetDrives().Where(delegate(DriveInfo d)
				{
					try
					{
						return d.IsReady && d.DriveType == DriveType.Removable && string.Equals(d.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) && d.TotalSize > 0 && d.TotalSize <= 134217728;
					}
					catch
					{
						return false;
					}
				})
				orderby Math.Abs(d.TotalSize - 52428800)
				select d.RootDirectory.FullName).FirstOrDefault() ?? "";
		}
		catch
		{
			return "";
		}
	}

	private static bool IsOnFat32DiagnosticsDrive(string path)
	{
		try
		{
			string? pathRoot = Path.GetPathRoot(Path.GetFullPath(path));
			if (string.IsNullOrWhiteSpace(pathRoot))
			{
				return false;
			}
			DriveInfo driveInfo = new DriveInfo(pathRoot);
			return driveInfo.IsReady && driveInfo.DriveType == DriveType.Removable && string.Equals(driveInfo.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) && driveInfo.TotalSize > 0 && driveInfo.TotalSize <= 134217728;
		}
		catch
		{
			return false;
		}
	}

	private static DiagnosticsResult ParseDiagnosticsLog(string path, string raw)
	{
		List<string> list = new List<string>();
		string value = "";
		bool flag = false;
		bool flag2 = false;
		string[] array = raw.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		HashSet<string> unansweredPromptCategories = array
			.Where(IsUnansweredDiagnosticsPrompt)
			.Select(DiagnosticsPromptCategory)
			.Where((string category) => !string.IsNullOrWhiteSpace(category))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		flag2 = array.Any(IsUnansweredDiagnosticsPrompt);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			Match match = Regex.Match(text, "^\\*\\*\\s*(.+?)\\s*\\*\\*$");
			if (match.Success)
			{
				value = HumanizeDiagnosticsText(match.Groups[1].Value);
				flag = false;
				continue;
			}
			if (IsUnansweredDiagnosticsPrompt(text))
			{
				flag2 = true;
				if (!string.IsNullOrWhiteSpace(value))
				{
					string promptTest = CondenseDiagnosticsFailure(value);
					if (list.Any((string f) => string.Equals(f, promptTest, StringComparison.OrdinalIgnoreCase)))
					{
						flag2 = true;
						flag = true;
						list.RemoveAll((string f) => string.Equals(f, promptTest, StringComparison.OrdinalIgnoreCase));
					}
				}
			}
			if (Regex.IsMatch(text, "^Test Result:\\s*Fail\\b", RegexOptions.IgnoreCase) && !string.IsNullOrWhiteSpace(value))
			{
				string currentTest = CondenseDiagnosticsFailure(value);
				list.RemoveAll((string f) => string.Equals(f, currentTest, StringComparison.OrdinalIgnoreCase));
				string failureCategory = DiagnosticsPromptCategory(currentTest);
				if (!flag && (string.IsNullOrWhiteSpace(failureCategory) || !unansweredPromptCategories.Contains(failureCategory)))
				{
					list.Add(currentTest);
				}
				continue;
			}
			if (Regex.IsMatch(text, "^Test Result:\\s*Success\\b", RegexOptions.IgnoreCase) && !string.IsNullOrWhiteSpace(value))
			{
				string currentTest = CondenseDiagnosticsFailure(value);
				list.RemoveAll((string f) => string.Equals(f, currentTest, StringComparison.OrdinalIgnoreCase));
				flag = false;
			}
		}
		if (list.RemoveAll((string failure) => IsUnansweredDiagnosticsPrompt(failure)) > 0)
		{
			flag2 = true;
		}
		list = list.Where((string f) => !string.IsNullOrWhiteSpace(f)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (flag2 && unansweredPromptCategories.Count == 0 && list.Count == 1)
		{
			// Some Dell log versions omit the test name from the unanswered-prompt event.
			// With a single corresponding failure, treat it as an incomplete prompt instead.
			list.Clear();
		}
		if (list.Count > 0)
		{
			string text2 = ((list.Count == 1) ? list[0] : string.Join("; ", list.Take(3)));
			if (list.Count > 3)
			{
				text2 += $"; plus {list.Count - 3} more";
			}
			if (flag2)
			{
				text2 += "; Technician did not respond to a diagnostics prompt.";
			}
			return new DiagnosticsResult("Bad", "Diagnostics failed", text2, path, raw);
		}
		if (flag2)
		{
			return new DiagnosticsResult("Warning", "Diagnostics not completed", "Technician did not respond to a diagnostics prompt.", path, raw);
		}
		if (!Regex.IsMatch(raw, "Test Result:\\s*Success\\b", RegexOptions.IgnoreCase))
		{
			return new DiagnosticsResult("Warning", "Diagnostics results unavailable", "No completed diagnostics results were detected in the log.", path, raw);
		}
		return new DiagnosticsResult("Ok", "Passed all diagnostics tests", "Dell preboot diagnostics reported no failed tests.", path, raw);
	}

	private static bool IsUnansweredDiagnosticsPrompt(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		return Regex.IsMatch(value, "(?:\\b(?:no|without)\\s+(?:user\\s+|technician\\s+)?(?:response|answer|feedback)\\b|\\b(?:user|technician)\\s+(?:did\\s+not|didn't|failed\\s+to)\\s+(?:respond|answer|provide\\s+(?:a\\s+)?(?:response|answer|feedback))\\b|\\b(?:response|answer|feedback)\\s+(?:was\\s+)?(?:not\\s+(?:received|provided|entered)|missing|unavailable)\\b|\\b(?:prompt|user interaction).*\\b(?:timed?\\s*out|timeout|unanswered)\\b|\\b(?:user|technician).*\\bno\\s+(?:input|selection)\\b)", RegexOptions.IgnoreCase);
	}

	private static string DiagnosticsPromptCategory(string value)
	{
		if (Regex.IsMatch(value, "\\b(?:video|graphics?|display|lcd|screen)\\b", RegexOptions.IgnoreCase)) return "Video";
		if (Regex.IsMatch(value, "\\b(?:audio|speaker|tone|sound)\\b", RegexOptions.IgnoreCase)) return "Audio";
		if (Regex.IsMatch(value, "\\b(?:camera|webcam)\\b", RegexOptions.IgnoreCase)) return "Camera";
		if (Regex.IsMatch(value, "\\b(?:keyboard|key)\\b", RegexOptions.IgnoreCase)) return "Keyboard";
		if (Regex.IsMatch(value, "\\b(?:touchpad|trackpad|mouse|pointer)\\b", RegexOptions.IgnoreCase)) return "PointingDevice";
		return "";
	}

	private static string HumanizeDiagnosticsText(string value)
	{
		return Regex.Replace(Regex.Replace(value.Trim(), "\\s+", " "), "\\s+-\\s+", " - ");
	}

	private static string CondenseDiagnosticsFailure(string value)
	{
		string input = HumanizeDiagnosticsText(value);
		input = Regex.Replace(input, "\\s*\\((?:Error|Validate code).+?\\)\\s*$", "", RegexOptions.IgnoreCase);
		input = Regex.Replace(input, "\\s+Error:\\s*[0-9:]+.*$", "", RegexOptions.IgnoreCase);
		input = Regex.Replace(input, "\\s+Validate code:\\s*\\d+.*$", "", RegexOptions.IgnoreCase);
		int num = input.IndexOf(" - ", StringComparison.Ordinal);
		if (num > 0)
		{
			input = input.Substring(0, num).Trim();
		}
		if (input.Length > 80)
		{
			return input.Substring(0, 77).TrimEnd() + "...";
		}
		return input;
	}

	private async Task<HardwareSnapshot> GetHardwareSnapshotAsync()
	{
		Task<string> currentComputerNameTask = GetCurrentWindowsComputerNameAsync();
		Dictionary<string, string> dictionary = JsonToDictionary(await PowerShellJsonAsync("function TextValue($value) {\n  if ($null -eq $value) { return '' }\n  $text = ([string]$value).Trim()\n  if ([string]::IsNullOrWhiteSpace($text)) { return '' }\n  return $text\n}\nfunction BoolValue($value) {\n  if ($null -eq $value) { return '' }\n  try { return ([bool]$value).ToString() } catch { return (TextValue $value) }\n}\nfunction SizeText($bytes) {\n  try {\n    $n = [double]$bytes\n    if ($n -le 0) { return '' }\n    $gb = [math]::Round($n / 1GB, 1)\n    if ($gb -eq [math]::Round($gb, 0)) { return ('{0:N0} GB' -f $gb) }\n    return ('{0:N1} GB' -f $gb)\n  } catch { return '' }\n}\nfunction DateText($value, $format = 'yyyy-MM-dd HH:mm') {\n  if ($null -eq $value) { return '' }\n  try { return ([datetime]$value).ToString($format) } catch { return (TextValue $value) }\n}\n$cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object -First 1\n$product = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction SilentlyContinue | Select-Object -First 1\n$baseBoard = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object -First 1\n$enclosure = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue | Select-Object -First 1\n$bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object -First 1\n$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object -First 1\n$cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1\n$gpu = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 1\n$drives = @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | ForEach-Object { '{0} {1:N1} GB' -f $_.Model, ($_.Size / 1GB) })\n$secureBoot = ''\ntry {\n  if (Get-Command Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) { $secureBoot = (Confirm-SecureBootUEFI).ToString() }\n} catch { $secureBoot = 'Unavailable' }\n$tpmPresent = ''\n$tpmReady = ''\n$tpmEnabled = ''\n$tpmActivated = ''\n$tpmVersion = ''\ntry {\n  if (Get-Command Get-Tpm -ErrorAction SilentlyContinue) {\n    $tpm = Get-Tpm\n    $tpmPresent = BoolValue $tpm.TpmPresent\n    $tpmReady = BoolValue $tpm.TpmReady\n    $tpmEnabled = BoolValue $tpm.TpmEnabled\n    $tpmActivated = BoolValue $tpm.TpmActivated\n    $tpmVersion = TextValue $tpm.ManufacturerVersionFull20\n  }\n} catch {}\n[pscustomobject]@{\n  Computer = [string]$env:COMPUTERNAME\n  Manufacturer = [string]$cs.Manufacturer\n  Model = [string]$cs.Model\n  SystemType = TextValue $cs.SystemType\n  Domain = TextValue $cs.Domain\n  PhysicalMemory = SizeText $cs.TotalPhysicalMemory\n  HypervisorPresent = BoolValue $cs.HypervisorPresent\n  ProductName = TextValue $product.Name\n  ProductVersion = TextValue $product.Version\n  Uuid = TextValue $product.UUID\n  Baseboard = (('{0} {1}' -f (TextValue $baseBoard.Manufacturer), (TextValue $baseBoard.Product)).Trim())\n  BaseboardSerial = TextValue $baseBoard.SerialNumber\n  ChassisManufacturer = TextValue $enclosure.Manufacturer\n  ChassisSerial = TextValue $enclosure.SerialNumber\n  ChassisAssetTag = TextValue $enclosure.SMBIOSAssetTag\n  BiosManufacturer = TextValue $bios.Manufacturer\n  SmbiosVersion = TextValue $bios.SMBIOSBIOSVersion\n  BiosVersion = (($bios.BIOSVersion | Where-Object { $_ }) -join ' / ')\n  BiosReleaseDate = DateText $bios.ReleaseDate 'yyyy-MM-dd'\n  BiosSerialNumber = TextValue $bios.SerialNumber\n  OsName = TextValue $os.Caption\n  OsVersion = TextValue $os.Version\n  OsBuild = TextValue $os.BuildNumber\n  OsArchitecture = TextValue $os.OSArchitecture\n  OsInstallDate = DateText $os.InstallDate\n  OsLastBoot = DateText $os.LastBootUpTime\n  SecureBootEnabled = $secureBoot\n  TpmPresent = $tpmPresent\n  TpmReady = $tpmReady\n  TpmEnabled = $tpmEnabled\n  TpmActivated = $tpmActivated\n  TpmManufacturerVersion = $tpmVersion\n  Cpu = [string]$cpu.Name\n  Memory = SizeText $cs.TotalPhysicalMemory\n  Gpu = [string]$gpu.Name\n  Storage = ($drives -join '; ')\n  Bios = [string]$bios.SMBIOSBIOSVersion\n} | ConvertTo-Json -Compress"));
		string internalStorage = await GetInternalStorageSummaryAsync();
		string currentComputerName = await currentComputerNameTask;
		return new HardwareSnapshot
		{
			Computer = ValueOrFallback(currentComputerName, dictionary.GetValueOrDefault("Computer", Environment.MachineName)),
			Manufacturer = dictionary.GetValueOrDefault("Manufacturer", ""),
			Model = dictionary.GetValueOrDefault("Model", ""),
			SystemType = dictionary.GetValueOrDefault("SystemType", ""),
			Domain = dictionary.GetValueOrDefault("Domain", ""),
			PhysicalMemory = dictionary.GetValueOrDefault("PhysicalMemory", ""),
			HypervisorPresent = dictionary.GetValueOrDefault("HypervisorPresent", ""),
			ProductName = dictionary.GetValueOrDefault("ProductName", ""),
			ProductVersion = dictionary.GetValueOrDefault("ProductVersion", ""),
			Uuid = dictionary.GetValueOrDefault("Uuid", ""),
			Baseboard = dictionary.GetValueOrDefault("Baseboard", ""),
			BaseboardSerial = dictionary.GetValueOrDefault("BaseboardSerial", ""),
			ChassisManufacturer = dictionary.GetValueOrDefault("ChassisManufacturer", ""),
			ChassisSerial = dictionary.GetValueOrDefault("ChassisSerial", ""),
			ChassisAssetTag = dictionary.GetValueOrDefault("ChassisAssetTag", ""),
			BiosManufacturer = dictionary.GetValueOrDefault("BiosManufacturer", ""),
			SmbiosVersion = dictionary.GetValueOrDefault("SmbiosVersion", ""),
			BiosVersion = dictionary.GetValueOrDefault("BiosVersion", ""),
			BiosReleaseDate = dictionary.GetValueOrDefault("BiosReleaseDate", ""),
			BiosSerialNumber = dictionary.GetValueOrDefault("BiosSerialNumber", ""),
			OsName = dictionary.GetValueOrDefault("OsName", ""),
			OsVersion = dictionary.GetValueOrDefault("OsVersion", ""),
			OsBuild = dictionary.GetValueOrDefault("OsBuild", ""),
			OsArchitecture = dictionary.GetValueOrDefault("OsArchitecture", ""),
			OsInstallDate = dictionary.GetValueOrDefault("OsInstallDate", ""),
			OsLastBoot = dictionary.GetValueOrDefault("OsLastBoot", ""),
			SecureBootEnabled = dictionary.GetValueOrDefault("SecureBootEnabled", ""),
			TpmPresent = dictionary.GetValueOrDefault("TpmPresent", ""),
			TpmReady = dictionary.GetValueOrDefault("TpmReady", ""),
			TpmEnabled = dictionary.GetValueOrDefault("TpmEnabled", ""),
			TpmActivated = dictionary.GetValueOrDefault("TpmActivated", ""),
			TpmManufacturerVersion = dictionary.GetValueOrDefault("TpmManufacturerVersion", ""),
			Cpu = dictionary.GetValueOrDefault("Cpu", ""),
			Memory = dictionary.GetValueOrDefault("Memory", ""),
			Gpu = dictionary.GetValueOrDefault("Gpu", ""),
			Storage = internalStorage,
			Bios = dictionary.GetValueOrDefault("Bios", "")
		};
	}

	private async Task<string> GetCurrentWindowsComputerNameAsync()
	{
		const string script = @"
$computerSystemName = ''
$configuredComputerName = ''
$activeComputerName = ''
$tcpipHostname = ''
$tcpipNvHostname = ''
try { $computerSystemName = [string](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop | Select-Object -First 1).Name } catch {}
try { $configuredComputerName = [string](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName' -ErrorAction Stop).ComputerName } catch {}
try { $activeComputerName = [string](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName' -ErrorAction Stop).ComputerName } catch {}
try {
  $tcpip = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters' -ErrorAction Stop
  $tcpipHostname = [string]$tcpip.Hostname
  $tcpipNvHostname = [string]$tcpip.'NV Hostname'
} catch {}
[pscustomobject]@{
  ConfiguredComputerName = $configuredComputerName
  ComputerSystemName = $computerSystemName
  ActiveComputerName = $activeComputerName
  TcpipHostname = $tcpipHostname
  TcpipNvHostname = $tcpipNvHostname
} | ConvertTo-Json -Compress
";
		try
		{
			Dictionary<string, string> names = JsonToDictionary(await PowerShellJsonAsync(script));
			return new string?[]
			{
				names.GetValueOrDefault("ConfiguredComputerName", ""),
				names.GetValueOrDefault("TcpipHostname", ""),
				names.GetValueOrDefault("TcpipNvHostname", ""),
				names.GetValueOrDefault("ComputerSystemName", ""),
				names.GetValueOrDefault("ActiveComputerName", ""),
				Environment.MachineName
			}.FirstOrDefault(IsUsefulFileIdentifier) ?? Environment.MachineName;
		}
		catch
		{
			return Environment.MachineName;
		}
	}

	private async Task<string> GetInternalStorageSummaryAsync()
	{
		const string script = @"
$externalBusTypes = @('USB', 'SD', 'MMC', '1394', 'iSCSI', 'File Backed Virtual')
$summaries = @(
  Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | ForEach-Object {
    $drive = $_
    $isExternal =
      ([string]$drive.InterfaceType -match '^(?i:USB|1394)$') -or
      ([string]$drive.PNPDeviceID -match '^(?i:USB|USBSTOR)') -or
      ([string]$drive.MediaType -match '(?i:removable|external)')

    try {
      $disk = Get-Disk -Number ([int]$drive.Index) -ErrorAction Stop
      if ($externalBusTypes -contains [string]$disk.BusType) { $isExternal = $true }
    } catch {}

    if (-not $isExternal -and [double]$drive.Size -gt 0) {
      $model = ([string]$drive.Model).Trim()
      if ([string]::IsNullOrWhiteSpace($model)) { $model = 'Internal storage' }
      '{0} {1:N1} GB' -f $model, ([double]$drive.Size / 1GB)
    }
  }
)
[pscustomobject]@{ Storage = ($summaries -join '; ') } | ConvertTo-Json -Compress
";
		try
		{
			return JsonToDictionary(await PowerShellJsonAsync(script)).GetValueOrDefault("Storage", "");
		}
		catch (Exception ex)
		{
			AddActivity("Hardware", "Internal storage inventory failed: " + ex.Message);
			return "";
		}
	}

	private async Task RefreshBiosAsync()
	{
		await RefreshBiosButtonStatesAsync(updateStatusText: true);
	}

	private async Task RefreshBiosButtonStatesAsync(bool updateStatusText)
	{
		Dictionary<string, string> states = _states;
		states["SecureBoot"] = await GetSecureBootStateAsync();
		SetBiosButtonState(BiosSecureBootButton, _states["SecureBoot"], "Secure Boot");
		SetBiosStatusIcon(_states["SecureBoot"]);
		if (updateStatusText)
		{
			BiosStatusText.Text = "Secure Boot " + StatePhrase(_states["SecureBoot"], "on", "off") + ".";
		}
		AddActivity("BIOS", "BIOS settings read: Secure Boot = " + _states["SecureBoot"] + ".");
	}

	private async Task<string> GetSecureBootStateAsync()
	{
		try
		{
			return JsonToDictionary(await PowerShellJsonAsync("$state = 'Unknown'\ntry {\n  if (Get-Command Confirm-SecureBootUEFI -ErrorAction SilentlyContinue) {\n    $state = if (Confirm-SecureBootUEFI) { 'Ok' } else { 'Bad' }\n  }\n} catch {\n  try {\n    $value = (Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State' -ErrorAction Stop).UEFISecureBootEnabled\n    $state = if ([int]$value -eq 1) { 'Ok' } else { 'Bad' }\n  } catch {}\n}\n[pscustomobject]@{ State = $state } | ConvertTo-Json -Compress")).GetValueOrDefault("State", "Unknown");
		}
		catch
		{
			return "Unknown";
		}
	}

	private async Task<string> GetPrimaryAcStateAsync()
	{
		if (!Directory.Exists(CommandPowerManagerDir))
		{
			return "Unknown";
		}
		string text = PsQuote(CommandPowerManagerDir);
		try
		{
			string valueOrDefault = JsonToDictionary(await PowerShellJsonAsync("$dir = '" + text + "'\nforeach ($dll in @('Utilities.dll','SmbLib.dll','SystemInterop.dll')) { [void][System.Reflection.Assembly]::LoadFrom((Join-Path $dir $dll)) }\n$smb = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SmbLib' } | Select-Object -First 1\n$type = $smb.GetType('Dell.CommandPowerManager.AcpiWmi.BatteryChargingMode', $true)\n$mode = [Activator]::CreateInstance($type)\n$value = [string]$mode.CurrentChargingMode\n[pscustomobject]@{ State = $value } | ConvertTo-Json -Compress")).GetValueOrDefault("State", "");
			return Regex.IsMatch(valueOrDefault, "PredominatelyAcUse|PredominantlyAcUse|PrimarilyAC|ACUse", RegexOptions.IgnoreCase) ? "Ok" : (string.IsNullOrWhiteSpace(valueOrDefault) ? "Unknown" : "Bad");
		}
		catch
		{
			return "Unknown";
		}
	}

	private async Task DisableDellOptimizerDynamicChargeAsync()
	{
		if (!File.Exists(DellOptimizerCliPath))
		{
			AddActivity("BIOS", "Dell Optimizer CLI not found; skipping Dynamic Charge disable step.");
			return;
		}
		try
		{
			await RunProcessCaptureAsync(DellOptimizerCliPath, "/configure -name=DynamicCharge.State -value=False", 20);
			AddActivity("BIOS", "Dell Optimizer Dynamic Charge disabled before Primary AC write.");
		}
		catch (Exception ex)
		{
			AddActivity("BIOS", "Dell Optimizer Dynamic Charge disable skipped: " + ex.Message);
		}
	}

	#endregion

	#region QA test actions and output

	private static string StatePhrase(string state, string ok, string bad)
	{
		if (!(state == "Ok"))
		{
			if (state == "Bad")
			{
				return bad;
			}
			return "unknown";
		}
		return ok;
	}

	private void SetBiosButtonState(Button button, string state, string text)
	{
		button.Content = text;
		button.Background = BiosButtonBrush(state);
	}

	private void SetBiosStatusIcon(string state)
	{
		TextBlock biosSecureBootIcon = BiosSecureBootIcon;
		biosSecureBootIcon.Text = state switch
		{
			"Ok" => "\u2713",
			"Bad" => "\u2715",
			"Warning" => "\u26A0",
			"Working" => "...",
			_ => "-",
		};
		BiosSecureBootIcon.Foreground = StepBrush(state);
	}

	private void RefreshStepIconBrushes()
	{
		if (WifiIcon != null)
		{
			WifiIcon.Foreground = StepBrush(_states.GetValueOrDefault("WiFi", "Waiting"));
		}
		if (EthernetIcon != null)
		{
			EthernetIcon.Foreground = StepBrush(_states.GetValueOrDefault("Ethernet", "Waiting"));
		}
		if (CameraIcon != null)
		{
			CameraIcon.Foreground = StepBrush(_states.GetValueOrDefault("Camera", "Waiting"));
		}
		if (ExternalIcon != null)
		{
			ExternalIcon.Foreground = StepBrush(_states.GetValueOrDefault("ExternalVideo", "Waiting"));
		}
		if (KeyboardIcon != null)
		{
			KeyboardIcon.Foreground = StepBrush(_states.GetValueOrDefault("Keyboard", "Waiting"));
		}
		if (DiagnosticsIcon != null)
		{
			DiagnosticsIcon.Foreground = StepBrush(_states.GetValueOrDefault("Diagnostics", "Warning"));
		}
	}

	private void SetStep(string key, TextBlock icon, TextBox main, TextBox detail, string state, string mainText, string detailText)
	{
		_states[key] = state;
		_details[key] = detailText;
		icon.Text = state switch
		{
			"Ok" => "\u2713",
			"Bad" => "\u2715",
			"Ignored" => "\u2298",
			"Warning" => "\u26A0",
			"Working" => "...",
			_ => "-",
		};
		icon.Foreground = StepBrush(state);
		main.Text = mainText;
		detail.Text = detailText;
		SaveQaSessionCache();
		CheckForQaCompletionCelebration();
	}

	private async void NetworkButton_Click(object sender, RoutedEventArgs e)
	{
		BeginProcessing("Network");
		NetworkButton.IsEnabled = false;
		SetStep("WiFi", WifiIcon, WifiMain, WifiDetail, "Working", "Checking Wi-Fi...", "Looking for a connected Wi-Fi IP or visible SSIDs.");
		SetStep("Ethernet", EthernetIcon, EthernetMain, EthernetDetail, "Working", "Checking Ethernet...", "Looking for an adapter with Status = Up.");
		AddActivity("Network", "Start selected; previous Wi-Fi and Ethernet results cleared and the network check restarted.");
		try
		{
			int value = Math.Max(0, _config.WifiRescanEthernetDisableDelaySeconds);
			int value2 = Math.Max(0, _config.EthernetRestoreDelaySeconds);
			Dictionary<string, string> dictionary = JsonToDictionary(await PowerShellJsonAsync($"$WifiDelaySeconds = {value}\n$EthernetRestoreDelaySeconds = {value2}\n\nfunction Test-UsableIpAddress {{\n  param([AllowNull()][string]$IPAddress)\n  if ([string]::IsNullOrWhiteSpace($IPAddress)) {{ return $false }}\n  $parsedAddress = [System.Net.IPAddress]::None\n  if (-not [System.Net.IPAddress]::TryParse($IPAddress.Trim(), [ref]$parsedAddress)) {{ return $false }}\n  if ([System.Net.IPAddress]::IsLoopback($parsedAddress)) {{ return $false }}\n  if ($parsedAddress.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {{\n    $bytes = $parsedAddress.GetAddressBytes()\n    return -not (($bytes[0] -eq 0) -or ($bytes[0] -eq 127) -or ($bytes[0] -eq 169 -and $bytes[1] -eq 254))\n  }}\n  if ($parsedAddress.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {{\n    return -not ($parsedAddress.IsIPv6LinkLocal -or $parsedAddress.IsIPv6Multicast -or $parsedAddress.Equals([System.Net.IPAddress]::IPv6None) -or $parsedAddress.Equals([System.Net.IPAddress]::IPv6Any))\n  }}\n  return $false\n}}\n\nfunction Get-AdapterUsableIpAddresses {{\n  param([Parameter(Mandatory=$true)][object]$Adapter)\n  try {{\n    return @(Get-NetIPAddress -InterfaceIndex $Adapter.ifIndex -ErrorAction SilentlyContinue |\n      Where-Object {{ Test-UsableIpAddress -IPAddress $_.IPAddress }} |\n      Sort-Object AddressFamily, IPAddress |\n      Select-Object -ExpandProperty IPAddress)\n  }} catch {{\n    return @()\n  }}\n}}\n\nfunction Get-WifiConnectionSummary {{\n  param([Parameter(Mandatory=$true)][object]$Adapter)\n  $ipAddresses = @(Get-AdapterUsableIpAddresses -Adapter $Adapter)\n  if ($ipAddresses.Count -eq 0) {{ return $null }}\n  [pscustomobject]@{{\n    Name = [string]$Adapter.Name\n    LinkSpeed = [string]$Adapter.LinkSpeed\n    Status = [string]$Adapter.Status\n    IpAddresses = $ipAddresses\n    Summary = (\"{{0}}: {{1}}\" -f $Adapter.Name, ($ipAddresses -join ', '))\n  }}\n}}\n\nfunction ConvertTo-CleanSsidName {{\n  param([AllowNull()][string]$Name)\n  if ([string]::IsNullOrWhiteSpace($Name)) {{ return '' }}\n\n  $text = $Name.Trim()\n  if ($text -match '(?i)\\\\x[0-9a-f]{{2}}') {{\n    $text = [regex]::Replace($text, '((?:\\\\x[0-9a-fA-F]{{2}})+)', {{\n      param($match)\n      $bytes = [byte[]]@([regex]::Matches($match.Value, '[0-9a-fA-F]{{2}}') | ForEach-Object {{ [Convert]::ToByte($_.Value, 16) }})\n      try {{\n        [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)\n      }} catch {{\n        [System.Text.Encoding]::Default.GetString($bytes)\n      }}\n    }})\n  }}\n\n  $text = [regex]::Replace($text, '[\\p{{Cc}}\\p{{Cf}}]', '')\n  return $text.Trim()\n}}\n\nfunction Get-NetworkStatus {{\n  $wifiOutput = @()\n  try {{\n    $wifiOutput = @(netsh wlan show networks mode=bssid 2>&1)\n  }} catch {{\n    $wifiOutput = @($_.Exception.Message)\n  }}\n\n  $ssids = @($wifiOutput | ForEach-Object {{\n    if ($_ -match '^\\s*SSID\\s+\\d+\\s*:\\s*(.+)$') {{ ConvertTo-CleanSsidName $Matches[1] }}\n  }} | Where-Object {{ $_ }} | Sort-Object -Unique)\n\n  $wifiAdapters = @()\n  try {{\n    $wifiAdapters = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object {{\n      ($_.Name -match '(?i)wi-?fi|wireless|wlan|802\\.11' -or $_.InterfaceDescription -match '(?i)wi-?fi|wireless|wlan|802\\.11') -and\n      $_.Name -notmatch '(?i)bluetooth' -and $_.InterfaceDescription -notmatch '(?i)bluetooth'\n    }} | Sort-Object Name)\n  }} catch {{\n    $wifiAdapters = @()\n  }}\n\n  $wifiUp = @($wifiAdapters | Where-Object {{ $_.Status -eq 'Up' }})\n  $wifiConnected = @($wifiAdapters | ForEach-Object {{ Get-WifiConnectionSummary -Adapter $_ }} | Where-Object {{ $null -ne $_ }})\n\n  $ethernetAdapters = @()\n  try {{\n    $ethernetAdapters = @(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object {{\n      $_.Name -notmatch '(?i)wi-?fi|wireless|wlan|bluetooth' -and $_.InterfaceDescription -notmatch '(?i)wi-?fi|wireless|wlan|bluetooth'\n    }} | Sort-Object Name)\n  }} catch {{\n    $ethernetAdapters = @()\n  }}\n\n  $ethernetUp = @($ethernetAdapters | Where-Object {{ $_.Status -eq 'Up' }})\n\n  [pscustomobject]@{{\n    Ssids = $ssids\n    WifiAdapters = $wifiAdapters\n    WifiConnected = $wifiConnected\n    WifiOk = ($ssids.Count -gt 0 -or $wifiConnected.Count -gt 0)\n    EthernetAdapters = $ethernetAdapters\n    EthernetUp = $ethernetUp\n    EthernetOk = $ethernetUp.Count -gt 0\n    Rescanned = $false\n    DisabledAdapters = @()\n    WifiRetryCount = 0\n  }}\n}}\n\nfunction Wait-ForWifiStatus {{\n  param(\n    [Parameter(Mandatory=$true)][pscustomobject]$Status,\n    [switch]$PreserveEthernet\n  )\n\n  $current = $Status\n  $retryDelaySeconds = [Math]::Max(1, $WifiDelaySeconds)\n  $retryCount = 0\n\n  for ($attempt = 1; $attempt -le 2 -and -not $current.WifiOk; $attempt++) {{\n    Start-Sleep -Seconds $retryDelaySeconds\n    $freshStatus = Get-NetworkStatus\n    $retryCount++\n\n    if ($PreserveEthernet) {{\n      $current = [pscustomobject]@{{\n        Ssids = $freshStatus.Ssids\n        WifiAdapters = $freshStatus.WifiAdapters\n        WifiConnected = $freshStatus.WifiConnected\n        WifiOk = $freshStatus.WifiOk\n        EthernetAdapters = $Status.EthernetAdapters\n        EthernetUp = $Status.EthernetUp\n        EthernetOk = $Status.EthernetOk\n        Rescanned = $Status.Rescanned\n        DisabledAdapters = $Status.DisabledAdapters\n        WifiRetryCount = $retryCount\n      }}\n    }} else {{\n      $current = $freshStatus\n      $current.WifiRetryCount = $retryCount\n    }}\n  }}\n\n  return $current\n}}\n\nfunction Invoke-WifiRescanIfOnlyEthernet {{\n  param([Parameter(Mandatory=$true)][pscustomobject]$Status)\n\n  if ($Status.WifiOk -or -not $Status.EthernetOk) {{ return $Status }}\n  $upAdapters = @($Status.EthernetUp)\n  if ($upAdapters.Count -eq 0) {{ return $Status }}\n\n  $disabledAdapterNames = New-Object System.Collections.Generic.List[string]\n  $restoreErrors = New-Object System.Collections.Generic.List[string]\n  $resultStatus = $Status\n\n  try {{\n    foreach ($adapter in $upAdapters) {{\n      try {{\n        Disable-NetAdapter -Name $adapter.Name -Confirm:$false -ErrorAction Stop | Out-Null\n        $disabledAdapterNames.Add($adapter.Name)\n      }} catch {{}}\n    }}\n\n    if ($disabledAdapterNames.Count -eq 0) {{ return $Status }}\n\n    Start-Sleep -Seconds $WifiDelaySeconds\n    $wifiRescanStatus = Get-NetworkStatus\n    $wifiRescanStatus = Wait-ForWifiStatus -Status $wifiRescanStatus\n    $resultStatus = [pscustomobject]@{{\n      Ssids = $wifiRescanStatus.Ssids\n      WifiAdapters = $wifiRescanStatus.WifiAdapters\n      WifiConnected = $wifiRescanStatus.WifiConnected\n      WifiOk = $wifiRescanStatus.WifiOk\n      EthernetAdapters = $Status.EthernetAdapters\n      EthernetUp = $Status.EthernetUp\n      EthernetOk = $Status.EthernetOk\n      Rescanned = $true\n      DisabledAdapters = @($disabledAdapterNames)\n      WifiRetryCount = $wifiRescanStatus.WifiRetryCount\n    }}\n  }} finally {{\n    foreach ($adapterName in $disabledAdapterNames) {{\n      try {{\n        Enable-NetAdapter -Name $adapterName -Confirm:$false -ErrorAction Stop | Out-Null\n      }} catch {{\n        $restoreErrors.Add((\"{{0}}: {{1}}\" -f $adapterName, $_.Exception.Message))\n      }}\n    }}\n    if ($disabledAdapterNames.Count -gt 0) {{ Start-Sleep -Seconds $EthernetRestoreDelaySeconds }}\n  }}\n\n  if ($restoreErrors.Count -gt 0) {{\n    throw \"Ethernet restore failed after Wi-Fi rescan. Re-enable manually if needed. $($restoreErrors -join ' | ')\"\n  }}\n\n  return $resultStatus\n}}\n\n$status = Invoke-WifiRescanIfOnlyEthernet -Status (Get-NetworkStatus)\n$status = Wait-ForWifiStatus -Status $status -PreserveEthernet\n\n$ssidText = (@($status.Ssids) | Select-Object -First 5) -join ', '\nif (@($status.Ssids).Count -gt 5) {{ $ssidText = \"$ssidText, and $(@($status.Ssids).Count - 5) more\" }}\n$connectedText = (@($status.WifiConnected) | ForEach-Object {{ $_.Summary }} | Where-Object {{ $_ }}) -join '; '\n\nif ($status.WifiOk) {{\n  if (@($status.WifiConnected).Count -gt 0 -and @($status.Ssids).Count -gt 0) {{\n    $wifi = \"Wi-Fi: connected; SSIDs visible ($(@($status.Ssids).Count))\"\n    $wifiDetail = \"$connectedText | SSIDs: $ssidText\"\n  }} elseif (@($status.WifiConnected).Count -gt 0) {{\n    $wifi = 'Wi-Fi: connected with IP'\n    $wifiDetail = $connectedText\n  }} else {{\n    $wifi = \"Wi-Fi: SSIDs visible ($(@($status.Ssids).Count))\"\n    $wifiDetail = $ssidText\n  }}\n}} else {{\n  $wifiAdapterText = (@($status.WifiAdapters) | ForEach-Object {{ \"{{0}}: {{1}}\" -f $_.Name, $_.Status }}) -join ', '\n  if (-not $wifiAdapterText) {{ $wifiAdapterText = 'No physical Wi-Fi adapters were found.' }}\n  $wifi = 'Wi-Fi: not connected and no SSIDs visible'\n  $wifiDetail = $wifiAdapterText\n}}\n\nif ($status.EthernetOk) {{\n  $upText = (@($status.EthernetUp) | ForEach-Object {{ \"{{0}} ({{1}})\" -f $_.Name, $_.LinkSpeed }}) -join ', '\n  $eth = 'Ethernet: at least one adapter is Up'\n  $ethDetail = $upText\n}} else {{\n  $adapterText = (@($status.EthernetAdapters) | ForEach-Object {{ \"{{0}}: {{1}}\" -f $_.Name, $_.Status }}) -join ', '\n  if (-not $adapterText) {{ $adapterText = 'No physical Ethernet adapters were found.' }}\n  $eth = 'Ethernet: no adapter is Up'\n  $ethDetail = $adapterText\n}}\n\n[pscustomobject]@{{\n  Wifi = $wifi\n  WifiDetail = $wifiDetail\n  WifiOk = [bool]$status.WifiOk\n  Ethernet = $eth\n  EthernetDetail = $ethDetail\n  EthernetOk = [bool]$status.EthernetOk\n  Rescanned = [bool]$status.Rescanned\n  DisabledAdapters = ((@($status.DisabledAdapters) | Where-Object {{ $_ }}) -join ', ')\n  SsidCount = [int]@($status.Ssids).Count\n  SsidSample = $ssidText\n  WifiConnectedCount = [int]@($status.WifiConnected).Count\n  WifiConnectedText = $connectedText\n  WifiAdaptersSeen = ((@($status.WifiAdapters) | ForEach-Object {{ \"{{0}}: {{1}}\" -f $_.Name, $_.Status }}) -join ', ')\n  WifiRetryCount = [int]$status.WifiRetryCount\n  EthernetUpCount = [int]@($status.EthernetUp).Count\n  EthernetUpText = $upText\n  EthernetAdaptersSeen = ((@($status.EthernetAdapters) | ForEach-Object {{ \"{{0}}: {{1}}\" -f $_.Name, $_.Status }}) -join ', ')\n}} | ConvertTo-Json -Compress"));
			bool result;
			bool flag = bool.TryParse(dictionary.GetValueOrDefault("WifiOk", ""), out result) && result;
			bool result2;
			bool flag2 = bool.TryParse(dictionary.GetValueOrDefault("EthernetOk", ""), out result2) && result2;
			int.TryParse(dictionary.GetValueOrDefault("SsidCount", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3);
			int.TryParse(dictionary.GetValueOrDefault("WifiConnectedCount", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result4);
			int.TryParse(dictionary.GetValueOrDefault("WifiRetryCount", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result5);
			int.TryParse(dictionary.GetValueOrDefault("EthernetUpCount", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result6);
			string wifiSpeedText = result4 > 0 ? await GetWifiLinkSpeedSummaryAsync() : "";
			string wifiDetail = dictionary.GetValueOrDefault("WifiDetail", "");
			if (!string.IsNullOrWhiteSpace(wifiSpeedText))
			{
				wifiDetail += " | Speed: " + wifiSpeedText;
			}
			string ethernetIpText = flag2 ? await GetEthernetIpSummaryAsync() : "";
			string ethernetDetail = dictionary.GetValueOrDefault("EthernetDetail", "");
			if (flag2)
			{
				ethernetDetail += string.IsNullOrWhiteSpace(ethernetIpText) ? " | IP unavailable" : " | IP: " + ethernetIpText;
			}
			SetStep("WiFi", WifiIcon, WifiMain, WifiDetail, flag ? "Ok" : "Bad", dictionary.GetValueOrDefault("Wifi", ""), wifiDetail);
			SetStep("Ethernet", EthernetIcon, EthernetMain, EthernetDetail, flag2 ? "Ok" : "Bad", dictionary.GetValueOrDefault("Ethernet", ""), ethernetDetail);
			if (flag)
			{
				if (result4 > 0 && result3 > 0)
				{
					AddActivity("Network", $"Wi-Fi connected with usable IP and SSIDs visible: {result3}. Connected: {dictionary.GetValueOrDefault("WifiConnectedText", "")}. Sample SSIDs: {dictionary.GetValueOrDefault("SsidSample", "")}");
				}
				else if (result4 > 0)
				{
					AddActivity("Network", "Wi-Fi connected with usable IP: " + dictionary.GetValueOrDefault("WifiConnectedText", ""));
				}
				else
				{
					AddActivity("Network", $"Wi-Fi SSIDs visible: {result3}. Sample: {dictionary.GetValueOrDefault("SsidSample", "")}");
				}
			}
			else
			{
				string valueOrDefault = dictionary.GetValueOrDefault("WifiAdaptersSeen", "");
				AddActivity("Network", string.IsNullOrWhiteSpace(valueOrDefault) ? "Wi-Fi failed: no usable Wi-Fi IP, 0 visible SSIDs, and no physical Wi-Fi adapters were found." : ("Wi-Fi failed: no usable Wi-Fi IP and 0 visible SSIDs. Seen: " + valueOrDefault));
			}
			if (!string.IsNullOrWhiteSpace(wifiSpeedText))
			{
				AddActivity("Network", "Wi-Fi link speed: " + wifiSpeedText + ".");
			}
			if (flag2)
			{
				AddActivity("Network", "Ethernet Up adapters: " + dictionary.GetValueOrDefault("EthernetUpText", "") + (string.IsNullOrWhiteSpace(ethernetIpText) ? "; usable IPv4 address unavailable." : "; IPv4: " + ethernetIpText + "."));
			}
			else
			{
				string valueOrDefault2 = dictionary.GetValueOrDefault("EthernetAdaptersSeen", "");
				AddActivity("Network", string.IsNullOrWhiteSpace(valueOrDefault2) ? "Ethernet Up adapters: 0. No physical Ethernet adapters were found." : ("Ethernet Up adapters: 0. Seen: " + valueOrDefault2));
			}
			if (bool.TryParse(dictionary.GetValueOrDefault("Rescanned", ""), out var result7) && result7)
			{
				string valueOrDefault3 = dictionary.GetValueOrDefault("DisabledAdapters", "");
				AddActivity("Network", string.IsNullOrWhiteSpace(valueOrDefault3) ? "Wi-Fi was rescanned while Ethernet was temporarily disabled." : ("Wi-Fi was rescanned while Ethernet was temporarily disabled: " + valueOrDefault3 + "."));
			}
			if (result5 > 0)
			{
				AddActivity("Network", $"Wi-Fi scan needed {result5} extra attempt{((result5 == 1) ? "" : "s")} before the final result.");
			}
			AddActivity("Network", "Network check completed.");
			if ((result4 > 0 || result6 > 0) && string.IsNullOrWhiteSpace(_warranty) && _warrantyWaitingForNetwork)
			{
				_warrantyWaitingForNetwork = false;
				AddActivity("Warranty", "Network is available; retrying the missing warranty lookup.");
				await RefreshWarrantyAsync();
				SaveQaSessionCache();
			}
		}
		catch (Exception ex)
		{
			SetStep("WiFi", WifiIcon, WifiMain, WifiDetail, "Bad", "Wi-Fi check failed", ex.Message);
			SetStep("Ethernet", EthernetIcon, EthernetMain, EthernetDetail, "Bad", "Ethernet check failed", ex.Message);
			AddActivity("Network", "Network check failed: " + ex.Message);
		}
		finally
		{
			NetworkButton.IsEnabled = true;
			EndProcessing("Network");
		}
	}

	private async Task<string> GetEthernetIpSummaryAsync()
	{
		const string script = @"
$items = @(
  Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Status -eq 'Up' -and
      $_.Name -notmatch '(?i)wi-?fi|wireless|wlan|bluetooth' -and
      $_.InterfaceDescription -notmatch '(?i)wi-?fi|wireless|wlan|bluetooth'
    } |
    Sort-Object Name |
    ForEach-Object {
      $adapter = $_
      $addresses = @(
        Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
          Where-Object {
            $_.IPAddress -and
            $_.IPAddress -notmatch '^(?:0\.|127\.|169\.254\.)'
          } |
          Select-Object -ExpandProperty IPAddress -Unique
      )
      if ($addresses.Count -gt 0) {
        '{0}: {1}' -f $adapter.Name, ($addresses -join ', ')
      }
    }
)
[pscustomobject]@{ Summary = ($items -join '; ') } | ConvertTo-Json -Compress
";
		try
		{
			Dictionary<string, string> result = JsonToDictionary(await PowerShellJsonAsync(script));
			return result.GetValueOrDefault("Summary", "").Trim();
		}
		catch
		{
			return "";
		}
	}

	private async Task<string> GetWifiLinkSpeedSummaryAsync()
	{
		const string script = @"
$items = @(
  Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
    Where-Object {
      $_.Status -eq 'Up' -and
      ($_.Name -match '(?i)wi-?fi|wireless|wlan|802\.11' -or $_.InterfaceDescription -match '(?i)wi-?fi|wireless|wlan|802\.11') -and
      $_.Name -notmatch '(?i)bluetooth' -and $_.InterfaceDescription -notmatch '(?i)bluetooth'
    } |
    Sort-Object Name |
    ForEach-Object { '{0}: {1}' -f $_.Name, $_.LinkSpeed }
)
[pscustomobject]@{ Summary = ($items -join '; ') } | ConvertTo-Json -Compress
";
		try
		{
			Dictionary<string, string> result = JsonToDictionary(await PowerShellJsonAsync(script));
			return result.GetValueOrDefault("Summary", "").Trim();
		}
		catch
		{
			return "";
		}
	}

	private async void CameraStartButton_Click(object sender, RoutedEventArgs e)
	{
		int cameraRunId = ++_cameraTestRunId;
		_cameraCleanupTask = null;
		BeginProcessing("Camera");
		SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Working", "Preparing camera test", "Setting camera audio defaults, then opening Camera.");
		AddActivity("Camera", "Start selected; previous camera result cleared and the test restarted.");
		try
		{
			string text = await RunAudioActionAsync("SetSpeaker");
			AddActivity("Camera", string.IsNullOrWhiteSpace(text) ? "Audio setup command completed." : ("Audio setup command completed: " + ShortActivityText(text)));
			Process.Start(new ProcessStartInfo("microsoft.windows.camera:")
			{
				UseShellExecute = true
			});
			SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Working", "Camera launched", "Close Camera when finished, then choose Pass or Fail.");
			AddActivity("Camera", "Camera app launch requested successfully.");
			_ = MonitorCameraCloseAndCleanupAsync(cameraRunId);
		}
		catch (Exception ex)
		{
			SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Bad", "Camera failed to launch", ex.Message);
			AddActivity("Camera", "Camera test failed: " + ex.Message);
			EndProcessing("Camera");
		}
	}

	private async void CameraPassButton_Click(object sender, RoutedEventArgs e)
	{
		await FinishCameraAsync("Ok", "Camera test passed", "Camera was manually marked Pass.");
	}

	private async void CameraFailButton_Click(object sender, RoutedEventArgs e)
	{
		await FinishCameraAsync("Bad", "Camera test failed", "Manual Fail selected.");
	}

	private async Task FinishCameraAsync(string state, string main, string detail)
	{
		try
		{
			string text = ((state == "Ok") ? "Pass" : "Fail");
			AddActivity("Camera", "Camera marked " + text + ".");
			string cleanupDetail = await EnsureCameraCleanupAsync(_cameraTestRunId);
			SetStep("Camera", CameraIcon, CameraMain, CameraDetail, state, main, detail + " " + cleanupDetail);
		}
		catch (Exception ex)
		{
			SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Bad", "Camera cleanup failed", ex.Message);
			AddActivity("Camera", "Camera cleanup failed: " + ex.Message);
		}
	}

	private Task<string> EnsureCameraCleanupAsync(int cameraRunId)
	{
		if (cameraRunId != _cameraTestRunId)
		{
			return Task.FromResult("");
		}
		return _cameraCleanupTask ??= PerformCameraCleanupAsync(cameraRunId);
	}

	private async Task<string> PerformCameraCleanupAsync(int cameraRunId)
	{
		try
		{
			AddActivity("Camera", "Restoring audio and cleaning Camera Roll.");
			string audioResult = await RunAudioActionAsync("Restore");
			AddActivity("Camera", string.IsNullOrWhiteSpace(audioResult) ? "Audio restore command completed." : ("Audio restore command completed: " + ShortActivityText(audioResult)));
			string cleanupResult = CleanupCameraRoll();
			AddActivity("Camera", "Camera cleanup completed successfully. " + cleanupResult);
			return "Audio restored and Camera Roll cleaned.";
		}
		finally
		{
			if (cameraRunId == _cameraTestRunId)
			{
				EndProcessing("Camera");
			}
		}
	}

	private async Task MonitorCameraCloseAndCleanupAsync(int cameraRunId)
	{
		bool cameraWindowSeen = false;
		int closedPolls = 0;
		DateTime detectionDeadline = DateTime.UtcNow.AddSeconds(20.0);
		while (cameraRunId == _cameraTestRunId && _cameraCleanupTask == null)
		{
			bool cameraVisible = IsWindowsCameraWindowVisible();
			if (cameraVisible)
			{
				cameraWindowSeen = true;
				closedPolls = 0;
			}
			else if (cameraWindowSeen)
			{
				closedPolls++;
				if (closedPolls >= 2)
				{
					break;
				}
			}
			else if (DateTime.UtcNow >= detectionDeadline)
			{
				AddActivity("Camera", "Camera-window monitoring was unavailable; cleanup will finish when Pass or Fail is selected.");
				return;
			}
			await Task.Delay(500);
		}
		if (!cameraWindowSeen || cameraRunId != _cameraTestRunId || _cameraCleanupTask != null)
		{
			return;
		}
		try
		{
			string cleanupDetail = await EnsureCameraCleanupAsync(cameraRunId);
			if (cameraRunId == _cameraTestRunId && _states.GetValueOrDefault("Camera", "") == "Working")
			{
				SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Working", "Camera closed; cleanup complete", cleanupDetail + " Choose Pass or Fail.");
			}
		}
		catch (Exception ex)
		{
			SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Bad", "Camera cleanup failed", ex.Message);
			AddActivity("Camera", "Camera cleanup failed: " + ex.Message);
		}
	}

	private static bool IsWindowsCameraWindowVisible()
	{
		try
		{
			foreach (Process process in Process.GetProcessesByName("WindowsCamera"))
			{
				using (process)
				{
					process.Refresh();
					if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private void ExternalIgnoreButton_Click(object sender, RoutedEventArgs e)
	{
		SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Ignored", "External video ignored", "Device does not have an external video port.");
		AddActivity("External Video", "External video marked Ignored because the device does not have an external video port.");
	}

	private void ExternalFailButton_Click(object sender, RoutedEventArgs e)
	{
		SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Bad", "External video failed", "Manual Fail selected.");
		AddActivity("External Video", "External video marked Fail.");
	}

	private async void KeyboardLaunchButton_Click(object sender, RoutedEventArgs e)
	{
		SetStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail, "Working", "Preparing keyboard test", "Clearing the previous result and opening the keyboard tester.");
		AddActivity("Keyboard", "Start selected; previous keyboard result cleared and the test restarted.");
		await SetFnLockForKeyboardTesterAsync(enabled: false, "disable before keyboard tester");
		try
		{
			KeyboardTesterWindow keyboardTesterWindow = new KeyboardTesterWindow(this, _currentTheme, _config.AppLanguage);
			keyboardTesterWindow.Closed += async delegate
			{
				AddActivity("Keyboard", "Keys pressed during test: " + keyboardTesterWindow.PressedKeysSummary + ".");
				AddActivity("Keyboard", "Keyboard tester closed; restoring Fn Lock.");
				await SetFnLockForKeyboardTesterAsync(enabled: true, "restore after keyboard tester");
			};
			keyboardTesterWindow.Show();
			SetStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail, "Working", "Keyboard tester launched", "Press keys in the tester, then choose Pass or Fail here.");
			AddActivity("Keyboard", "Keyboard tester launch requested; waiting for manual Pass or Fail.");
		}
		catch
		{
			await SetFnLockForKeyboardTesterAsync(enabled: true, "restore after keyboard tester launch failure");
			throw;
		}
	}

	private async Task SetFnLockForKeyboardTesterAsync(bool enabled, string phase)
	{
		if (!File.Exists(CctkExe))
		{
			AddActivity("Keyboard", "Fn Lock " + phase + " skipped: Dell CCTK was not found.");
			return;
		}
		string value = (enabled ? "enable" : "disable");
		string text = "";
		string[] array = new string[2] { "fnlockmode", "fnlock" };
		foreach (string option in array)
		{
			try
			{
				await RunProcessCaptureAsync(CctkExe, "--" + option + "=" + value, 20);
				AddActivity("Keyboard", $"Fn Lock {phase} succeeded with CCTK {option}={value}.");
				return;
			}
			catch (Exception ex)
			{
				text = ex.Message;
				AddActivity("Keyboard", $"Fn Lock {phase} attempt failed with CCTK {option}: {ex.Message}");
			}
		}
		AddActivity("Keyboard", "Fn Lock " + phase + " failed: " + text);
	}

	private void KeyboardPassButton_Click(object sender, RoutedEventArgs e)
	{
		SetStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail, "Ok", "Keyboard test passed", "Keyboard was manually marked Pass.");
		AddActivity("Keyboard", "Keyboard marked Pass.");
	}

	private void KeyboardFailButton_Click(object sender, RoutedEventArgs e)
	{
		SetStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail, "Bad", "Keyboard test failed", "Manual Fail selected.");
		AddActivity("Keyboard", "Keyboard marked Fail.");
	}

	private async void HashButton_Click(object sender, RoutedEventArgs e)
	{
		string? temporaryPath = null;
		try
		{
			AddActivity("Hash", "Autopilot hash collection started.");
			CleanupOldFiles(HashDir, 90, "Hash", "hash file(s)");
			Directory.CreateDirectory(HashDir);
			temporaryPath = Path.Combine(HashDir, $".hash-{Guid.NewGuid():N}.tmp.csv");
			if (!File.Exists(AutopilotHashScript))
			{
				throw new FileNotFoundException("The packaged Autopilot hash script was not found.", AutopilotHashScript);
			}
			string text = PsQuote(temporaryPath);
			string groupTag = string.IsNullOrWhiteSpace(_config.AutopilotGroupTag) ? "LNG AAD" : _config.AutopilotGroupTag.Trim();
			string groupTagValue = PsQuote(groupTag);
			string scriptPath = PsQuote(AutopilotHashScript);
			Dictionary<string, string> result = JsonToDictionary(await PowerShellJsonAsync("$InformationPreference = 'SilentlyContinue'\n$ProgressPreference = 'SilentlyContinue'\n& '" + scriptPath + "' -OutputFile '" + text + "' -GroupTag '" + groupTagValue + "' | Out-Null\nif (-not (Test-Path -LiteralPath '" + text + "' -PathType Leaf)) { throw 'The Autopilot hash script did not create an output file.' }\n$row = Import-Csv -LiteralPath '" + text + "' | Select-Object -First 1\nif ($null -eq $row) { throw 'The Autopilot hash CSV did not contain a device row.' }\n[pscustomobject]@{\n  Serial = [string]$row.'Device Serial Number'\n  HardwareHash = [string]$row.'Hardware Hash'\n  GroupTag = [string]$row.'Group Tag'\n} | ConvertTo-Json -Compress"));
			string serial = result.GetValueOrDefault("Serial", "").Trim();
			if (!IsUsefulFileIdentifier(serial))
			{
				throw new InvalidOperationException("The generated hash did not contain a valid Device Serial Number.");
			}
			if (string.IsNullOrWhiteSpace(result.GetValueOrDefault("HardwareHash", "")))
			{
				throw new InvalidOperationException("The generated CSV did not contain a hardware hash.");
			}
			if (!string.Equals(result.GetValueOrDefault("GroupTag", "").Trim(), groupTag, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("The generated CSV did not contain the configured group tag: " + groupTag);
			}
			string path = Path.Combine(HashDir, $"{SafeFile(serial, "unknown")}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-AutopilotHash.csv");
			File.Move(temporaryPath, path, overwrite: true);
			temporaryPath = null;
			FinalHashGroupTagCheck.IsChecked = true;
			AddActivity("Hash", "Hash saved for serial " + serial + ": " + path);
			if (MessageBox.Show(this, "Hash saved using the Device Serial Number stored inside the CSV:\n\n" + path + "\n\nOpen the Hash folder now?", "Get Hash", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
			{
				OpenManagedFolder(HashDir, "Hash");
			}
		}
		catch (Exception ex)
		{
			AddActivity("Hash", "Hash collection failed: " + ex.Message);
			MessageBox.Show(this, "Hash creation failed:\n" + ex.Message, "Get Hash", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(temporaryPath))
			{
				try { File.Delete(temporaryPath); } catch { }
			}
		}
	}

	private void QaSheetButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			FinalizePendingUsbPortsForQaSheet();
			QaSheetFiles qaSheetFiles = SaveQaSheet();
			AddActivity("QA Sheet", "QA sheet PNG saved: " + qaSheetFiles.PngPath);
			OpenQaSheet(qaSheetFiles.PngPath);
			AddActivity("QA Sheet", "QA sheet opened: " + qaSheetFiles.PngPath);
		}
		catch (Exception ex)
		{
			AddActivity("QA Sheet", "QA sheet failed: " + ex.Message);
			MessageBox.Show(this, "QA sheet failed:\n" + ex.Message, "QA Sheet", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void ServiceNowButton_Click(object sender, RoutedEventArgs e)
	{
		string requestDescription = BuildServiceNowRequestDescription();
		try
		{
			string requestUrl = GetServiceNowRequestUrl();
			string bookmarklet = BuildServiceNowBookmarklet(requestDescription, GetServiceNowTypeOfRequest(), GetServiceNowAssignmentGroupSysId(), GetServiceNowAssignmentGroupName());
			Clipboard.SetText(bookmarklet);
			RunServiceNowAutomation(requestUrl, GetServiceNowAutomationDelayMilliseconds(), requestDescription);
			AddActivity("ServiceNow", "ServiceNow automatic form fill started for the configured request, type, and assignment group.");
		}
		catch (Exception ex)
		{
			try
			{
				ServiceNowRequestLauncher.OpenRequestWithClipboard(GetServiceNowRequestUrl(), requestDescription);
				AddActivity("ServiceNow", "Automatic form fill could not start; request opened with QA details copied to the clipboard. " + ex.Message);
				MessageBox.Show(this, "Automatic ServiceNow form fill could not start. The request page was opened and the QA request details were copied to the clipboard.\n\n" + ex.Message, "ServiceNow Fallback", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			catch (Exception fallbackEx)
			{
				AddActivity("ServiceNow", "ServiceNow request launch failed: " + fallbackEx.Message);
				MessageBox.Show(this, "ServiceNow could not be opened. Open the configured request URL manually.\n\n" + fallbackEx.Message, "ServiceNow Request", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
	}

	private QaSheetFiles SaveQaSheet()
	{
		CleanupOldFiles(QaDir, 90, "QA Sheet", "QA sheet file(s)");
		SaveQaSessionCache();
		QaSessionCache cache = ReadQaSessionCache() ?? throw new InvalidOperationException("The cached QA session could not be read. Save the QA state and try again.");
		string text = $"{CachedQaComputerName(cache)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-QA-Sheet";
		string pngPath = Path.Combine(QaDir, text + ".png");
		RenderQaSheetPngInternal(pngPath, cache);
		return new QaSheetFiles(pngPath);
	}

	private void CleanupQaSheetHtmlFiles()
	{
		if (!Directory.Exists(QaDir))
		{
			return;
		}
		int num = 0;
		string[] files = Directory.GetFiles(QaDir, "*.html", SearchOption.TopDirectoryOnly);
		foreach (string path in files)
		{
			try
			{
				File.Delete(path);
				num++;
			}
			catch (Exception ex)
			{
				AddActivity("QA Sheet", "Could not delete temporary HTML file " + Path.GetFileName(path) + ": " + ex.Message);
			}
		}
		if (num > 0)
		{
			AddActivity("QA Sheet", $"Removed {num} temporary QA sheet HTML file(s).");
		}
	}

	private void RenderQaSheetPng(string htmlPath, string pngPath)
	{
		string? edgePath = GetEdgePath();
		if (string.IsNullOrWhiteSpace(edgePath))
		{
			throw new InvalidOperationException("Microsoft Edge was not found, so the QA sheet PNG could not be created.");
		}
		string text = Path.Combine(RuntimeDir, "edge-qa-render-" + Guid.NewGuid().ToString("N"));
		string text2 = Path.Combine(RuntimeDir, "qa-render-" + Guid.NewGuid().ToString("N") + ".png");
		Directory.CreateDirectory(RuntimeDir);
		Directory.CreateDirectory(text);
		ProcessStartInfo processStartInfo = new ProcessStartInfo(edgePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		processStartInfo.ArgumentList.Add("--headless");
		processStartInfo.ArgumentList.Add("--disable-gpu");
		processStartInfo.ArgumentList.Add("--hide-scrollbars");
		processStartInfo.ArgumentList.Add("--start-minimized");
		processStartInfo.ArgumentList.Add("--force-device-scale-factor=2");
		processStartInfo.ArgumentList.Add("--default-background-color=00000000");
		AddEdgePromptSuppressionArguments(processStartInfo);
		processStartInfo.ArgumentList.Add("--user-data-dir=" + text);
		processStartInfo.ArgumentList.Add("--window-size=768,1500");
		processStartInfo.ArgumentList.Add("--screenshot=" + text2);
		processStartInfo.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);
		try
		{
			using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Microsoft Edge did not start for QA sheet PNG rendering.");
			if (!process.WaitForExit(30000))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				throw new TimeoutException("QA sheet PNG rendering timed out.");
			}
			for (int i = 0; i < 20; i++)
			{
				if (File.Exists(text2) && new FileInfo(text2).Length > 0)
				{
					break;
				}
				Thread.Sleep(100);
			}
			if (process.ExitCode != 0 || !File.Exists(text2) || new FileInfo(text2).Length == 0L)
			{
				throw new InvalidOperationException($"QA sheet PNG rendering failed with exit code {process.ExitCode}.");
			}
			File.Copy(text2, pngPath, overwrite: true);
		}
		finally
		{
			try
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			catch
			{
			}
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private void RenderQaSheetPngInternal(string pngPath, QaSessionCache cache)
	{
		HardwareSnapshot hardware = cache.Hardware ?? new HardwareSnapshot();
		List<QaRenderRow> list = BuildQaSheetRenderRows(cache);
		string overall = (list.Any((QaRenderRow r) => r.State == "Bad") ? "Needs Attention" : (list.Any((QaRenderRow r) => r.State == "Warning") ? "Warning" : (list.All((QaRenderRow r) => r.State == "Ok" || r.State == "Ignored") ? "Passed" : "Incomplete")));
		double[] array = list.Select((QaRenderRow row) => Math.Max(44.0, Math.Min(78.0, MeasureQaText(row.Detail, 282.0, 11.4, FontWeights.Normal) + 18.0))).ToArray();
		double num = 31.0 + array.Sum();
		double num2 = Math.Max(1040.0, 345.0 + num + 18.0 + 190.0 + 38.0);
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.PushTransform(new ScaleTransform(2.0, 2.0));
			Rect rect = new Rect(0.0, 0.0, 768.0, num2);
			RectangleGeometry clipGeometry = new RectangleGeometry(rect, 16.0, 16.0);
			drawingContext.PushClip(clipGeometry);
			drawingContext.DrawRoundedRectangle(Brushes.White, null, rect, 16.0, 16.0);
			LinearGradientBrush brush = new LinearGradientBrush(ColorFromHex("#18333D"), ColorFromHex("#5F858D"), new Point(0.0, 0.0), new Point(1.0, 1.0));
			drawingContext.DrawRectangle(brush, null, new Rect(0.0, 0.0, 768.0, 76.0));
			DrawQaText(drawingContext, L("Laptop QA Testing"), 26.0, 22.0, 420.0, 30.0, 24.0, Brushes.White, FontWeights.ExtraBold);
			DrawQaOverall(drawingContext, overall, 612.0, 15.0);
			double num3 = 92.0;
			double num4 = (716.0 - 24.0) / 4.0;
			(string, string)[] array2 = new(string, string)[8]
			{
				(L("Device Name"), CachedQaComputerName(cache)),
				(L("Technician"), _config.TechnicianName),
				(L("Date"), DateTime.Now.ToString("g", CultureInfo.CurrentCulture)),
				(L("Manufacturer"), hardware.Manufacturer),
				(L("Model"), hardware.Model),
				(L("Service Tag"), cache.ServiceTag),
				(L("Asset Number"), cache.AssetTag),
				(L("Warranty"), WarrantyDisplayText(cache.Warranty))
			};
			for (int num5 = 0; num5 < array2.Length; num5++)
			{
				int num6 = num5 % 4;
				int num7 = num5 / 4;
				DrawQaField(drawingContext, array2[num5].Item1, array2[num5].Item2, 26.0 + (double)num6 * (num4 + 8.0), num3 + (double)(num7 * 50), num4, 44.0);
			}
			num3 += 119.0;
			DrawQaSectionTitle(drawingContext, L("Hardware Specs"), 26.0, num3);
			num3 += 19.0;
			DrawHardwareSpecs(drawingContext, hardware, 26.0, num3, 716.0);
			num3 += 86.0;
			DrawQaSectionTitle(drawingContext, L("QA Results"), 26.0, num3);
			num3 += 21.0;
			DrawQaTable(drawingContext, list, array, 26.0, num3, 716.0);
			num3 += num + 17.0;
			DrawQaSectionTitle(drawingContext, L("Notes"), 26.0, num3);
			num3 += 22.0;
			DrawQaNote(drawingContext, L("RMA Issues"), cache.RmaIssues ?? "", 26.0, num3, 716.0, 74.0);
			num3 += 82.0;
			DrawQaNote(drawingContext, L("Repair Notes"), cache.RepairNotes ?? "", 26.0, num3, 716.0, 100.0);
			num3 += 114.0;
			Pen pen = new Pen(BrushFromHex("#D7E1E5"), 1.0);
			drawingContext.DrawLine(pen, new Point(26.0, num3), new Point(742.0, num3));
			DrawQaText(drawingContext, $"{L("Generated")}: {DateTime.Now:G}", 26.0, num3 + 8.0, 320.0, 18.0, 9.5, BrushFromHex("#60757E"), FontWeights.Normal);
			drawingContext.Pop();
			drawingContext.Pop();
		}
		int pixelWidth = (int)Math.Ceiling(1536.0);
		int pixelHeight = (int)Math.Ceiling(num2 * 2.0);
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		Directory.CreateDirectory(Path.GetDirectoryName(pngPath) ?? QaDir);
		using FileStream stream = File.Create(pngPath);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		pngBitmapEncoder.Save(stream);
	}

	private List<QaRenderRow> BuildQaSheetRenderRows(QaSessionCache cache)
	{
		cache.Steps ??= new Dictionary<string, QaStepCache>();
		cache.UsbPorts ??= new List<UsbPortCache>();
		string text = ((cache.FinalHashGroupTag == true) ? "Ok" : "Waiting");
		string text2 = ((cache.FinalCleanedLaptop == true) ? "Ok" : "Waiting");
		string text3 = ((cache.FinalDeletedUser == true) ? "Ok" : "Waiting");
		string text4 = ((cache.FinalUpdateStockrooms == true) ? "Ok" : "Waiting");
		string text5 = ((cache.FinalTrackpadWorking == true) ? "Ok" : "Waiting");
		string text6 = ((cache.FinalConditionSuitableForUse == true) ? "Ok" : "Waiting");
		string state = ((!cache.UsbPortTestFinished) ? "Waiting" : (cache.UsbPorts.Any((UsbPortCache port) => port.Failed) ? "Bad" : "Ok"));
		string detail = ((cache.UsbPorts.Count == 0) ? "USB port count unavailable from BIOS connector data." : $"{cache.UsbPorts.Count((UsbPortCache port) => port.Passed)} passed, {cache.UsbPorts.Count((UsbPortCache port) => port.Failed)} failed, {cache.UsbPorts.Count((UsbPortCache port) => !port.Passed && !port.Failed)} pending.");
		return new List<QaRenderRow>
		{
			new QaRenderRow("2", L("Wi-Fi connected or SSIDs visible"), StateFor("WiFi"), L(DetailFor("WiFi", "Wi-Fi not checked yet. Looking for a connected Wi-Fi IP or visible SSIDs."))),
			new QaRenderRow("2", L("Ethernet adapter is Up"), StateFor("Ethernet"), L(DetailFor("Ethernet", "Ethernet not checked yet. Looking for at least one physical Ethernet adapter that is Up."))),
			new QaRenderRow("3", L("Camera, audio restore, and Camera Roll cleanup"), StateFor("Camera"), L(DetailFor("Camera", "Camera not checked yet. Start Camera, then choose Pass or Fail."))),
			new QaRenderRow("4", L("External display video verified"), StateFor("ExternalVideo"), L(DetailFor("ExternalVideo", "External video not checked yet. Verify video output on the external display."))),
			new QaRenderRow("5", L("Keyboard test result"), StateFor("Keyboard"), L(DetailFor("Keyboard", "Keyboard not checked yet. Start tester, then choose Pass or Fail."))),
			new QaRenderRow("6", L("Dell preboot diagnostics"), StateFor("Diagnostics", "Warning"), L(DetailFor("Diagnostics", "Diagnostics log not found."))),
			new QaRenderRow("7", L("USB ports verified"), state, detail),
			new QaRenderRow("", L("Battery health checked"), "Ok", cache.BatterySummary ?? ""),
			new QaRenderRow("8", L("Hash and group tag checked"), text, L((text == "Ok") ? "Hash and group tag checked off." : "Hash and group tag not checked off.")),
			new QaRenderRow("8", L("Laptop cleaned"), text2, L((text2 == "Ok") ? "Cleaned laptop checked off." : "Cleaned laptop not checked off.")),
			new QaRenderRow("8", L("Removed User from Laptop in Intune"), text3, L((text3 == "Ok") ? "User removal from laptop in Intune checked off." : "User removal from laptop in Intune not checked off.")),
			new QaRenderRow("8", L("Update Stockrooms"), text4, L((text4 == "Ok") ? "Stockrooms updated." : "Stockrooms not updated.")),
			new QaRenderRow("8", L("Trackpad working"), text5, L((text5 == "Ok") ? "Trackpad working checked off." : "Trackpad working not checked off.")),
			new QaRenderRow("8", L("Physical condition suitable for use"), text6, L((text6 == "Ok") ? "Physical laptop condition confirmed suitable for use." : "Physical laptop condition not confirmed suitable for use."))
		};
		string StateFor(string key, string fallback = "Waiting")
		{
			return cache.Steps.TryGetValue(key, out QaStepCache? step) && !string.IsNullOrWhiteSpace(step.State) ? step.State : fallback;
		}
		string DetailFor(string key, string fallback)
		{
			if (!cache.Steps.TryGetValue(key, out QaStepCache? step) || step == null || StateFor(key) == "Waiting") return fallback;
			return string.IsNullOrWhiteSpace(step.DetailText) ? fallback : step.DetailText;
		}
	}

	private void DrawQaOverall(DrawingContext dc, string overall, double x, double y)
	{
		dc.DrawRoundedRectangle(rectangle: new Rect(x, y, 130.0, 48.0), brush: BrushFromHex("#24FFFFFF"), pen: new Pen(BrushFromHex("#55FFFFFF"), 1.0), radiusX: 8.0, radiusY: 8.0);
		DrawQaText(dc, L("OVERALL"), x, y + 9.0, 130.0, 12.0, 9.5, BrushFromHex("#D8E8EC"), FontWeights.ExtraBold, TextAlignment.Center);
		DrawQaText(dc, L(overall), x, y + 23.0, 130.0, 20.0, 15.5, Brushes.White, FontWeights.ExtraBold, TextAlignment.Center);
	}

	private void DrawQaField(DrawingContext dc, string label, string value, double x, double y, double width, double height)
	{
		dc.DrawRoundedRectangle(BrushFromHex("#F7FAFB"), new Pen(BrushFromHex("#CBD9DF"), 1.0), new Rect(x, y, width, height), 7.0, 7.0);
		DrawQaText(dc, label.ToUpperInvariant(), x + 8.0, y + 7.0, width - 16.0, 11.0, 8.8, BrushFromHex("#52666F"), FontWeights.ExtraBold);
		DrawQaText(dc, value, x + 8.0, y + 23.0, width - 16.0, 16.0, 11.2, BrushFromHex("#13252D"), FontWeights.Bold);
	}

	private void HeaderClockTimer_Tick(object? sender, EventArgs e)
	{
		UpdateHeaderDateTime();
	}

	private void UpdateHeaderDateTime()
	{
		if (HeaderDateTime != null)
		{
			HeaderDateTime.Text = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
		}
	}

	private void DrawHardwareSpecs(DrawingContext dc, HardwareSnapshot hardware, double x, double y, double width)
	{
		dc.DrawRoundedRectangle(BrushFromHex("#FBFCFD"), new Pen(BrushFromHex("#CBD9DF"), 1.0), new Rect(x, y, width, 76.0), 7.0, 7.0);
		double num = (width - 28.0) / 2.0;
		double num2 = x + 10.0;
		double num3 = x + 10.0 + num + 18.0;
		dc.DrawLine(new Pen(BrushFromHex("#E2EAED"), 1.0), new Point(num2, y + 29.0), new Point(num2 + num, y + 29.0));
		dc.DrawLine(new Pen(BrushFromHex("#E2EAED"), 1.0), new Point(num3, y + 29.0), new Point(num3 + num, y + 29.0));
		dc.DrawLine(new Pen(BrushFromHex("#E2EAED"), 1.0), new Point(num2, y + 52.0), new Point(x + width - 10.0, y + 52.0));
		DrawSpec("CPU", hardware.Cpu, num2, y + 7.0, num - 72.0);
		DrawSpec("Memory", ValueOrFallback(hardware.Memory, hardware.PhysicalMemory), num3, y + 7.0, num - 72.0);
		DrawSpec("GPU", hardware.Gpu, num2, y + 30.0, num - 72.0);
		DrawSpec("Storage", hardware.Storage, num2, y + 54.0, width - 92.0, 17.0);
		void DrawSpec(string label, string value, double sx, double sy, double valueWidth, double valueHeight = 17.0)
		{
			DrawQaText(dc, label.ToUpperInvariant(), sx, sy, 70.0, 12.0, 8.6, BrushFromHex("#52666F"), FontWeights.ExtraBold);
			DrawQaText(dc, value, sx + 72.0, sy, valueWidth, valueHeight, 10.3, BrushFromHex("#13252D"), FontWeights.Bold);
		}
	}

	private void DrawQaTable(DrawingContext dc, IReadOnlyList<QaRenderRow> rows, IReadOnlyList<double> rowHeights, double x, double y, double width)
	{
		double num = 312.0;
		double num2 = 102.0;
		double num3 = width - num - num2;
		Pen pen = new Pen(BrushFromHex("#D7E1E5"), 1.0);
		SolidColorBrush brush = BrushFromHex("#244F5C");
		dc.DrawRectangle(brush, null, new Rect(x, y, width, 31.0));
		DrawQaText(dc, L("TASK"), x + 8.0, y + 9.0, num - 16.0, 12.0, 10.0, Brushes.White, FontWeights.ExtraBold);
		DrawQaText(dc, L("STATUS"), x + num + 8.0, y + 9.0, num2 - 16.0, 12.0, 10.0, Brushes.White, FontWeights.ExtraBold);
		DrawQaText(dc, L("DETAIL"), x + num + num2 + 8.0, y + 9.0, num3 - 16.0, 12.0, 10.0, Brushes.White, FontWeights.ExtraBold);
		double num4 = y + 31.0;
		for (int i = 0; i < rows.Count; i++)
		{
			QaRenderRow qaRenderRow = rows[i];
			double num5 = rowHeights[i];
			if (i % 2 == 1)
			{
				dc.DrawRectangle(BrushFromHex("#F6F9FA"), null, new Rect(x, num4, width, num5));
			}
			dc.DrawRectangle(null, pen, new Rect(x, num4, width, num5));
			dc.DrawLine(pen, new Point(x + num, num4), new Point(x + num, num4 + num5));
			dc.DrawLine(pen, new Point(x + num + num2, num4), new Point(x + num + num2, num4 + num5));
			DrawQaText(dc, qaRenderRow.Task, x + 8.0, num4 + 10.0, num - 16.0, num5 - 14.0, 11.5, BrushFromHex("#13252D"), FontWeights.ExtraBold);
			DrawStatusPill(dc, qaRenderRow.State, x + num + 10.0, num4 + (num5 - 24.0) / 2.0, num2 - 20.0, 24.0);
			DrawQaText(dc, qaRenderRow.Detail, x + num + num2 + 8.0, num4 + 8.0, num3 - 16.0, num5 - 12.0, 11.2, BrushFromHex("#13252D"), FontWeights.Normal);
			num4 += num5;
		}
	}

	private void DrawStatusPill(DrawingContext dc, string state, double x, double y, double width, double height)
	{
		var (text, hex, hex2, hex3) = state switch
		{
			"Ok" => ("PASS", "#0F5132", "#D9F5E6", "#A9E6C1"),
			"Bad" => ("FAIL", "#842029", "#FDE2E4", "#F3B4BB"),
			"Ignored" => ("IGNORED", "#465A62", "#EEF3F5", "#CCD8DE"),
			"Warning" => ("CAUTION", "#6B4D00", "#FFF2C2", "#F2D36B"),
			"Working" => ("IN PROGRESS", "#614A00", "#FFF2C2", "#F2D36B"),
			_ => ("NOT RUN", "#465A62", "#EEF3F5", "#CCD8DE"),
		};
		dc.DrawRoundedRectangle(BrushFromHex(hex2), new Pen(BrushFromHex(hex3), 1.0), new Rect(x, y, width, height), 12.0, 12.0);
		DrawQaText(dc, L(text), x, y + 5.0, width, 11.0, 9.5, BrushFromHex(hex), FontWeights.ExtraBold, TextAlignment.Center);
	}

	private void DrawQaNote(DrawingContext dc, string label, string value, double x, double y, double width, double height)
	{
		dc.DrawRoundedRectangle(BrushFromHex("#FBFCFD"), new Pen(BrushFromHex("#CBD9DF"), 1.0), new Rect(x, y, width, height), 7.0, 7.0);
		DrawQaText(dc, label.ToUpperInvariant(), x + 9.0, y + 8.0, width - 18.0, 12.0, 9.5, BrushFromHex("#52666F"), FontWeights.ExtraBold);
		DrawQaText(dc, value, x + 9.0, y + 28.0, width - 18.0, height - 34.0, 12.0, BrushFromHex("#13252D"), FontWeights.SemiBold);
	}

	private void DrawQaSectionTitle(DrawingContext dc, string title, double x, double y)
	{
		DrawQaText(dc, title.ToUpperInvariant(), x, y, 300.0, 16.0, 13.0, BrushFromHex("#18333D"), FontWeights.ExtraBold);
	}

	private string L(string text)
	{
		return UiLocalization.Text(_config.AppLanguage, text);
	}

	private double MeasureQaText(string text, double width, double fontSize, FontWeight weight)
	{
		FormattedText formattedText = CreateQaFormattedText(text, fontSize, BrushFromHex("#13252D"), weight);
		formattedText.MaxTextWidth = width;
		return formattedText.Height;
	}

	private void DrawQaText(DrawingContext dc, string? text, double x, double y, double width, double height, double fontSize, Brush brush, FontWeight weight, TextAlignment alignment = TextAlignment.Left)
	{
		FormattedText formattedText = CreateQaFormattedText(text ?? "", fontSize, brush, weight);
		formattedText.MaxTextWidth = Math.Max(1.0, width);
		formattedText.Trimming = TextTrimming.CharacterEllipsis;
		formattedText.TextAlignment = alignment;
		formattedText.MaxTextHeight = Math.Max(height, Math.Min(formattedText.Height + 3.0, height + fontSize * 1.4));
		dc.DrawText(formattedText, new Point(x, y));
	}

	private FormattedText CreateQaFormattedText(string text, double fontSize, Brush brush, FontWeight weight)
	{
		FlowDirection flowDirection = string.Equals(_config.AppLanguage, "ar-SA", StringComparison.OrdinalIgnoreCase) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
		return new FormattedText(text, CultureInfo.CurrentCulture, flowDirection, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
	}

	private void OpenQaSheet(string path)
	{
		try
		{
			QaSessionCache? cachedSession = ReadQaSessionCache();
			string cachedServiceTag = cachedSession?.ServiceTag ?? _serviceTag;
			new QaSheetImageWindow(this, path, _currentTheme, _config.AppLanguage, cachedServiceTag).Show();
			return;
		}
		catch (Exception ex)
		{
			AddActivity("QA Sheet", "Borderless QA sheet viewer failed, opening with Windows instead: " + ex.Message);
		}
		Process.Start(new ProcessStartInfo(path)
		{
			UseShellExecute = true
		});
	}

	private void CleanupEdgeQaProfiles()
	{
		if (!Directory.Exists(RuntimeDir))
		{
			return;
		}
		string[] directories = Directory.GetDirectories(RuntimeDir, "edge-qa-*", SearchOption.TopDirectoryOnly);
		foreach (string path in directories)
		{
			try
			{
				if (Directory.GetLastWriteTime(path) < DateTime.Now.AddHours(-8.0))
				{
					Directory.Delete(path, recursive: true);
				}
			}
			catch
			{
			}
		}
		directories = Directory.GetFiles(RuntimeDir, "qa-render-*.png", SearchOption.TopDirectoryOnly);
		foreach (string path2 in directories)
		{
			try
			{
				File.Delete(path2);
			}
			catch
			{
			}
		}
	}

	private static void AddEdgePromptSuppressionArguments(ProcessStartInfo startInfo)
	{
		startInfo.ArgumentList.Add("--no-first-run");
		startInfo.ArgumentList.Add("--no-default-browser-check");
		startInfo.ArgumentList.Add("--disable-search-engine-choice-screen");
		startInfo.ArgumentList.Add("--disable-sync");
		startInfo.ArgumentList.Add("--disable-background-networking");
		startInfo.ArgumentList.Add("--disable-component-update");
		startInfo.ArgumentList.Add("--disable-extensions");
		startInfo.ArgumentList.Add("--disable-features=msEdgeFirstRunExperience,msEdgeOnRampFRE,msEdgeWelcomePage,msImplicitSignin,EdgeIdentityConsentedAccount,msRewards,msDiscoverChatInSidebar,msEdgeSidebarV2,EdgeShoppingAssistant");
	}

	private static string? GetEdgePath()
	{
		string[] array = new string[2];
		string? environmentVariable = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
		object obj;
		if (environmentVariable == null || environmentVariable.Length <= 0)
		{
			obj = "";
		}
		else
		{
			InlineArray5<string> buffer = default(InlineArray5<string>);
			buffer[0] = environmentVariable;
			buffer[1] = "Microsoft";
			buffer[2] = "Edge";
			buffer[3] = "Application";
			buffer[4] = "msedge.exe";
			obj = Path.Combine(buffer);
		}
		array[0] = (string)obj;
		string? environmentVariable2 = Environment.GetEnvironmentVariable("ProgramFiles");
		object obj2;
		if (environmentVariable2 == null || environmentVariable2.Length <= 0)
		{
			obj2 = "";
		}
		else
		{
			InlineArray5<string> buffer2 = default(InlineArray5<string>);
			buffer2[0] = environmentVariable2;
			buffer2[1] = "Microsoft";
			buffer2[2] = "Edge";
			buffer2[3] = "Application";
			buffer2[4] = "msedge.exe";
			obj2 = Path.Combine(buffer2);
		}
		array[1] = (string)obj2;
		return array.FirstOrDefault(File.Exists);
	}

	private void HardwareButton_Click(object sender, RoutedEventArgs e)
	{
		_hardwareOpen = !_hardwareOpen;
		if (_hardwareOpen)
		{
			HardwareDetailsBox.Text = HardwareDetailText();
		}
		UpdateDrawerLayout();
		AddActivity("Hardware", _hardwareOpen ? "Hardware drawer shown." : "Hardware drawer hidden.");
	}

	private void DiagnosticsRawButton_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_diagnosticsRawText))
		{
			MessageBox.Show(this, "No diagnostics log is loaded yet.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		AddActivity("Diagnostics", string.IsNullOrWhiteSpace(_diagnosticsLogPath) ? "Raw diagnostics log opened." : ("Raw diagnostics log opened: " + _diagnosticsLogPath));
		new HardwareWindow(this, _diagnosticsRawText, _serviceTag, RuntimeDir, _currentTheme, _config.AppLanguage, null, "Diagnostics Log", showSave: false).ShowDialog();
	}

	private async void DiagnosticsBrowseButton_Click(object sender, RoutedEventArgs e)
	{
		string text = FindDiagnosticsBrowseStartFolder();
		if (string.IsNullOrWhiteSpace(text) || !Directory.Exists(text))
		{
			AddActivity("Diagnostics", "Diagnostics browse unavailable: no FAT32 diagnostics drive was detected.");
			MessageBox.Show(this, "No FAT32 diagnostics drive was detected. Connect the diagnostics drive and try again.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Select Dell diagnostics log",
			InitialDirectory = (Directory.Exists(text) ? text : ""),
			FileName = "DellPrebootDiagnosticsLog.txt",
			Filter = "Dell diagnostics log (DellPrebootDiagnosticsLog.txt)|DellPrebootDiagnosticsLog.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*",
			FilterIndex = 3,
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog(this) != true)
		{
			AddActivity("Diagnostics", "Diagnostics log browse canceled.");
			return;
		}
		if (!IsOnFat32DiagnosticsDrive(openFileDialog.FileName))
		{
			AddActivity("Diagnostics", "Diagnostics log rejected because it is not on the FAT32 diagnostics drive: " + openFileDialog.FileName);
			MessageBox.Show(this, "Select the diagnostics log from the small FAT32 diagnostics drive.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		DiagnosticsBrowseButton.IsEnabled = false;
		SetStep("Diagnostics", DiagnosticsIcon, DiagnosticsMain, DiagnosticsDetail, "Working", "Loading diagnostics log...", "Loading " + openFileDialog.FileName);
		AddActivity("Diagnostics", "Diagnostics log selected: " + openFileDialog.FileName);
		try
		{
			await ApplyStartupDiagnosticsAsync(GetDiagnosticsResultFromPathAsync(openFileDialog.FileName));
		}
		finally
		{
			DiagnosticsBrowseButton.IsEnabled = true;
		}
	}

	private async void BiosLoadDefaultsButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			BiosLoadDefaultsButton.Content = "Writing...";
			await PowerShellAsync("function statusText($s) {\n  switch ([string]$s) { '0' { 'Success'; break } '1' { 'Failed'; break } '2' { 'Invalid parameter'; break } '3' { 'Access denied'; break } '4' { 'Not supported'; break } default { [string]$s } }\n}\n$messages = New-Object System.Collections.Generic.List[string]\n\ntry {\n  $setupPath = 'HKLM:\\SYSTEM\\Setup'\n  $childPath = 'HKLM:\\SYSTEM\\Setup\\Status\\ChildCompletion'\n  $setup = Get-ItemProperty -Path $setupPath -ErrorAction SilentlyContinue\n  $isSetupUser = [string]::Equals([Environment]::UserName, 'defaultuser0', [StringComparison]::OrdinalIgnoreCase)\n  $oobeInProgress = if ($null -ne $setup -and $null -ne $setup.OOBEInProgress) { [int]$setup.OOBEInProgress } else { 0 }\n  $systemSetupInProgress = if ($null -ne $setup -and $null -ne $setup.SystemSetupInProgress) { [int]$setup.SystemSetupInProgress } else { 0 }\n  $setupPhase = if ($null -ne $setup -and $null -ne $setup.SetupPhase) { [int]$setup.SetupPhase } else { 0 }\n  $setupInProgress = $isSetupUser -or\n    ($oobeInProgress -eq 1 -or $systemSetupInProgress -eq 1 -or $setupPhase -ne 0)\n\n  if ($setupInProgress) {\n    New-Item -Path $childPath -Force | Out-Null\n    Set-ItemProperty -Path $childPath -Name 'setup.exe' -Type DWord -Value 3 -ErrorAction Stop\n    $messages.Add(\"Windows setup/OOBE resume guard was applied before factory defaults.\") | Out-Null\n  }\n} catch {\n  throw \"Windows setup/OOBE resume guard failed before factory defaults: $($_.Exception.Message)\"\n}\n\ntry {\n  $iface = Get-CimInstance -Namespace 'root\\dcim\\sysman\\biosattributes' -ClassName 'BIOSAttributeInterface' -ErrorAction Stop | Select-Object -First 1\n  if ($iface) {\n    $method = $iface.CimClass.CimClassMethods['SetBIOSDefaults']\n    if ($method) {\n      $attempts = @(\n        @{ SecType=[uint32]0; SecHndCount=[uint32]0; SecHandle=[byte[]]@(); DefaultType=[uint32]2 },\n        @{ SecType=[uint32]0; SecHndCount=[uint32]0; SecHandle=[byte[]]@(); DefaultType='FactoryDefaults' },\n        @{ SecType=[uint32]0; SecHndCount=[uint32]0; SecHandle=[byte[]]@(); DefaultType='Factory' }\n      )\n      foreach ($args in $attempts) {\n        try {\n          $result = Invoke-CimMethod -InputObject $iface -MethodName 'SetBIOSDefaults' -Arguments $args -ErrorAction Stop\n          $status = if ($null -ne $result.Status) { statusText $result.Status } elseif ($null -ne $result.ReturnValue) { statusText $result.ReturnValue } else { 'Success' }\n          if ($status -eq 'Success') { exit 0 }\n          $messages.Add(\"BIOSAttributeInterface SetBIOSDefaults factory attempt returned $status\") | Out-Null\n        } catch { $messages.Add(\"BIOSAttributeInterface SetBIOSDefaults factory attempt failed: $($_.Exception.Message)\") | Out-Null }\n      }\n    } else {\n      $messages.Add(\"BIOSAttributeInterface does not expose SetBIOSDefaults.\") | Out-Null\n    }\n  }\n} catch { $messages.Add(\"BIOSAttributeInterface factory defaults unavailable: $($_.Exception.Message)\") | Out-Null }\n\ntry {\n  $service = Get-CimInstance -Namespace 'root\\dcim\\sysman' -ClassName 'DCIM_BIOSService' -ErrorAction Stop | Select-Object -First 1\n  if ($service) {\n    $result = Invoke-CimMethod -InputObject $service -MethodName 'ResetBIOSDefaults' -Arguments @{ DefaultType=[uint32]2 } -ErrorAction Stop\n    $status = if ($null -ne $result.Status) { statusText $result.Status } elseif ($null -ne $result.ReturnValue) { statusText $result.ReturnValue } else { 'Success' }\n    if ($status -eq 'Success') { exit 0 }\n    $messages.Add(\"DCIM_BIOSService ResetBIOSDefaults DefaultType=2 returned $status\") | Out-Null\n  }\n} catch { $messages.Add(\"DCIM_BIOSService ResetBIOSDefaults DefaultType=2 failed: $($_.Exception.Message)\") | Out-Null }\n\nthrow (($messages | Select-Object -First 10) -join '; ')");
			BiosStatusText.Text = "Factory defaults request was written. Reboot this laptop to complete the change.";
			AddActivity("BIOS", "Factory defaults request was written.");
			await RefreshBiosButtonStatesAsync(updateStatusText: false);
		}
		catch (Exception ex)
		{
			BiosStatusText.Text = "Factory defaults request failed: " + ex.Message;
			AddActivity("BIOS", "Factory defaults write failed: " + ex.Message);
			await RefreshBiosButtonStatesAsync(updateStatusText: false);
		}
		finally
		{
			BiosLoadDefaultsButton.Content = "Factory";
		}
	}

	private async void BiosSecureBootButton_Click(object sender, RoutedEventArgs e)
	{
		if (_states["SecureBoot"] == "Ok")
		{
			AddActivity("BIOS", "Secure Boot write skipped because Secure Boot is already on.");
			MessageBox.Show(this, "Secure Boot is already on.\n\nDisabling Secure Boot must be done from BIOS setup.", "Secure Boot", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		try
		{
			SetBiosButtonState(BiosSecureBootButton, "Working", "Writing...");
			SetBiosStatusIcon("Working");
			AddActivity("BIOS", "Secure Boot enable request started.");
			await PowerShellAsync("function statusText($s) {\n  switch ([string]$s) { '0' { 'Success'; break } '1' { 'Failed'; break } '2' { 'Invalid parameter'; break } '3' { 'Access denied'; break } '4' { 'Not supported'; break } default { [string]$s } }\n}\n$names = @('SecureBoot','SecureBootEnable','Secure Boot','Secure Boot Enable')\n$values = @('Enabled','Enable','On','1')\n$messages = New-Object System.Collections.Generic.List[string]\ntry {\n  $attrs = @(Get-CimInstance -Namespace 'root\\dcim\\sysman\\biosattributes' -ClassName 'EnumerationAttribute' -ErrorAction Stop)\n  foreach ($attr in $attrs) { $n = [string]$attr.AttributeName; if ($n -match '(?i)secure\\s*boot|secureboot') { $names = @($n) + $names } }\n} catch { $messages.Add(\"Lookup failed: $($_.Exception.Message)\") | Out-Null }\nforeach ($name in ($names | Select-Object -Unique)) {\n  foreach ($value in $values) {\n    try {\n      $iface = Get-CimInstance -Namespace 'root\\dcim\\sysman\\biosattributes' -ClassName 'BIOSAttributeInterface' -ErrorAction Stop | Select-Object -First 1\n      if ($iface) {\n        $result = Invoke-CimMethod -InputObject $iface -MethodName SetAttribute -Arguments @{ SecType=[uint32]0; SecHndCount=[uint32]0; SecHandle=[byte[]]@(); AttributeName=$name; AttributeValue=$value } -ErrorAction Stop\n        $status = statusText $result.Status\n        if ($status -eq 'Success') { exit 0 }\n        $messages.Add(\"$name=$value returned $status\") | Out-Null\n      }\n    } catch { $messages.Add(\"$name=$value failed: $($_.Exception.Message)\") | Out-Null }\n  }\n}\nthrow (($messages | Select-Object -First 8) -join '; ')");
			_states["SecureBoot"] = "Ok";
			SetBiosButtonState(BiosSecureBootButton, "Ok", "Secure Boot");
			SetBiosStatusIcon("Ok");
			BiosStatusText.Text = "Secure Boot request was written. Reboot is required.";
			AddActivity("BIOS", "Secure Boot enable request written. The button was updated to enabled; reboot is required before Windows reports the active firmware state.");
			if (_qaSessionReady)
			{
				SaveQaSessionCache();
			}
		}
		catch (Exception ex)
		{
			Dictionary<string, string> states = _states;
			states["SecureBoot"] = await GetSecureBootStateAsync();
			SetBiosButtonState(BiosSecureBootButton, _states["SecureBoot"], "Secure Boot");
			SetBiosStatusIcon(_states["SecureBoot"]);
			BiosStatusText.Text = "Secure Boot request failed: " + ex.Message;
			AddActivity("BIOS", "Secure Boot write failed: " + ex.Message);
			await RefreshBiosButtonStatesAsync(updateStatusText: false);
		}
	}

	#endregion

	#region USB port detection and scoring

	private void AttachUsbDeviceChangeHook()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			_windowSource = HwndSource.FromHwnd(handle);
			_windowSource?.AddHook(UsbDeviceWindowProc);
			_usbDeviceChangeDebounceTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(175L)
			};
			_usbDeviceChangeDebounceTimer.Tick += async delegate
			{
				_usbDeviceChangeDebounceTimer?.Stop();
				await RefreshConnectedDockObservationsAsync();
				await PollUsbPortsAsync();
			};
		}
	}

	private nint UsbDeviceWindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
	{
		if (message != 537 || !_usbPortTestActive)
		{
			return IntPtr.Zero;
		}
		int num = ((IntPtr)wParam).ToInt32();
		if ((num == 7 || num == 32768 || num == 32772) ? true : false)
		{
			_usbDeviceChangeDebounceTimer?.Stop();
			_usbDeviceChangeDebounceTimer?.Start();
		}
		return IntPtr.Zero;
	}

	private async Task InitializeUsbPortTestAsync()
	{
		_ = 3;
		try
		{
			_usbPortPollTimer?.Stop();
			_usbPortTestActive = false;
			_usbPortTestFinished = false;
			_usbPortDetectionAdjustment = "";
			List<string> list = await DetectExpectedUsbPortsAsync();
			_usbPorts.Clear();
			_usbPorts.AddRange(list.Select((string _, int index) => new UsbPortCache
			{
				Label = $"USB {index + 1}"
			}));
			_states["UsbPorts"] = "Waiting";
			_usbPortPollTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(550L)
			};
			_usbPortPollTimer.Tick += async delegate
			{
				await PollUsbPortsAsync();
			};
			AddActivity("USB", (list.Count <= 0) ? "BIOS connector data did not provide a usable external USB port count." : (string.IsNullOrWhiteSpace(_usbPortDetectionAdjustment) ? $"BIOS connector data reported {list.Count} external USB port(s): {string.Join(", ", list)}." : $"{_usbPortDetectionAdjustment} USB test configured for {list.Count} physical port(s): {string.Join(", ", list)}."));
			await RefreshConnectedDockObservationsAsync();
			await RestartUsbPortMonitoringAsync(clearResults: false);
			await PollUsbPortsAsync();
		}
		catch (Exception ex)
		{
			_usbPorts.Clear();
			_states["UsbPorts"] = "Warning";
			AddActivity("USB", "USB connector detection failed: " + ex.Message);
		}
		UpdateUsbPortUi();
	}

	private async Task<List<string>> DetectExpectedUsbPortsAsync()
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(await PowerShellJsonAsync("$model = [string](Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Model)\n$ports = @(Get-CimInstance Win32_PortConnector -ErrorAction SilentlyContinue |\n    Where-Object {\n        $connectorTypes = @($_.ConnectorType)\n        $_.ExternalReferenceDesignator -and\n        $_.ExternalReferenceDesignator -ne 'None' -and\n        ($_.ExternalReferenceDesignator -match 'USB' -or $_.PortType -in @(16,35) -or $connectorTypes -contains 18 -or $connectorTypes -contains 35)\n    } |\n    Select-Object ExternalReferenceDesignator,PortType,ConnectorType)\n[pscustomobject]@{ SystemModel = $model; Ports = @($ports) } | ConvertTo-Json -Compress -Depth 4"));
		List<string> list = new List<string>();
		string text = _hardware.Model ?? "";
		int num = 0;
		int num2 = 0;
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.ValueKind == JsonValueKind.Object && rootElement.TryGetProperty("SystemModel", out var modelElement))
		{
			text = modelElement.GetString() ?? text;
		}
		JsonElement[] array = (rootElement.ValueKind == JsonValueKind.Object && rootElement.TryGetProperty("Ports", out var portsElement)) ? ((portsElement.ValueKind == JsonValueKind.Array) ? portsElement.EnumerateArray().ToArray() : ((portsElement.ValueKind == JsonValueKind.Object) ? new JsonElement[1] { portsElement } : Array.Empty<JsonElement>())) : Array.Empty<JsonElement>();
		for (int i = 0; i < array.Length; i++)
		{
			JsonElement jsonElement = array[i];
			JsonElement value2;
			string text2 = (jsonElement.TryGetProperty("ExternalReferenceDesignator", out value2) ? (value2.GetString() ?? "") : "");
			JsonElement value3;
			int value4;
			int num3 = ((jsonElement.TryGetProperty("PortType", out value3) && value3.TryGetInt32(out value4)) ? value4 : 0);
			JsonElement value5;
			int[] array2 = ((jsonElement.TryGetProperty("ConnectorType", out value5) && value5.ValueKind == JsonValueKind.Array) ? (from jsonElement2 in value5.EnumerateArray()
				where jsonElement2.TryGetInt32(out var _)
				select jsonElement2.GetInt32()).ToArray() : ((value5.ValueKind == JsonValueKind.Number && value5.TryGetInt32(out var connectorType)) ? new int[1] { connectorType } : Array.Empty<int>()));
			string[] array3 = text2.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (array3.Length == 0)
			{
				array3 = new string[1] { text2 };
			}
			string[] array4 = array3;
			foreach (string text3 in array4)
			{
				bool flag = !text3.Contains("USB", StringComparison.OrdinalIgnoreCase);
				if (flag)
				{
					bool flag2 = ((num3 == 16 || num3 == 18 || num3 == 35) ? true : false);
					flag = !flag2;
				}
				if (!flag)
				{
					bool num5 = array2.Contains(35) || num3 == 35 || text3.Contains("USBC", StringComparison.OrdinalIgnoreCase) || text3.Contains("USB-C", StringComparison.OrdinalIgnoreCase) || text3.Contains("TYPE C", StringComparison.OrdinalIgnoreCase);
					string text4 = ((!num5) ? (++num) : (++num2)).ToString(CultureInfo.InvariantCulture);
					bool flag3 = num3 == 35 || text3.Contains("THUNDERBOLT", StringComparison.OrdinalIgnoreCase);
					string item = ((!num5) ? "USB-A" : (flag3 ? "USB-C/TB" : "USB-C")) + " " + text4;
					list.Add(item);
				}
			}
		}
		if (Regex.IsMatch(text, "\\bPrecision\\s+(?:5560|5570)\\b", RegexOptions.IgnoreCase) && list.Count != 3)
		{
			int count = list.Count;
			list = new List<string>(3) { "USB-C/TB 1", "USB-C/TB 2", "USB-C 3" };
			_usbPortDetectionAdjustment = $"{text.Trim()} profile replaced unavailable or duplicate USB/Thunderbolt BIOS entries (BIOS count {count}; physical count 3).";
		}
		if (Regex.IsMatch(text, "\\bLatitude\\s+(?:5320|5330|5430|5440|5450)\\b", RegexOptions.IgnoreCase) && !text.Contains("Rugged", StringComparison.OrdinalIgnoreCase) && list.Count != 4)
		{
			int count = list.Count;
			list = new List<string>(4) { "USB-A 1", "USB-A 2", "USB-C/TB 1", "USB-C/TB 2" };
			_usbPortDetectionAdjustment = $"{text.Trim()} profile replaced unavailable or duplicate USB/Thunderbolt BIOS entries (BIOS count {count}; physical count 4).";
		}
		return list;
	}

	private Task RestartUsbPortMonitoringAsync(bool clearResults)
	{
		if (clearResults)
		{
			_usbPortTestFinished = false;
			foreach (UsbPortCache usbPort in _usbPorts)
			{
				usbPort.Passed = false;
				usbPort.Failed = false;
				usbPort.LocationPath = "";
				usbPort.DeviceName = "";
			}
		}
		_usbPreviousPresentPaths.Clear();
		_usbPortTestActive = _qaLiveMonitoringActive && _usbPorts.Count > 0;
		_states["UsbPorts"] = ((_usbPorts.Count == 0) ? "Warning" : ((!_usbPortTestFinished) ? "Working" : (_usbPorts.Any((UsbPortCache port) => port.Failed) ? "Bad" : "Ok")));
		if (_usbPortTestActive)
		{
			_usbPortPollTimer?.Start();
			AddActivity("USB", $"Continuous USB monitoring active for {_usbPorts.Count} detected port(s) and will remain active until Laptop QA closes. Connected docks count once; move a readable thumb drive between the remaining laptop ports.");
		}
		else
		{
			_usbPortPollTimer?.Stop();
		}
		UpdateUsbPortUi();
		return Task.CompletedTask;
	}

	private async Task PollUsbPortsAsync()
	{
		if (!_usbPortTestActive || _usbPortScanRunning)
		{
			return;
		}
		_usbPortScanRunning = true;
		try
		{
			List<UsbPortObservation> source = (from @group in (await GetReadableUsbStorageAsync()).Concat(_usbDockObservations).Select(NormalizeUsbObservationToConnectedDock).GroupBy<UsbPortObservation, string>((UsbPortObservation item) => item.Path, StringComparer.OrdinalIgnoreCase)
				select @group.FirstOrDefault((UsbPortObservation item) => item.Name.Contains("Dock", StringComparison.OrdinalIgnoreCase)) ?? @group.First()).ToList();
			if (!_usbPortTestActive)
			{
				return;
			}
			HashSet<string> other = source.Select((UsbPortObservation item) => item.Path).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<UsbPortObservation> list = source.Where((UsbPortObservation item) => !_usbPreviousPresentPaths.Contains(item.Path)).ToList();
			List<string> removedPaths = _usbPreviousPresentPaths.Where((string path) => !other.Contains(path)).ToList();
			foreach (UsbPortObservation item in list)
			{
				AddActivity("USB", $"Detected USB port activity from {item.Name}; upstream topology {item.Path}.");
			}
			foreach (string removedPath in removedPaths)
			{
				AddActivity("USB", $"USB device removed from upstream topology {removedPath}.");
			}
			_usbPreviousPresentPaths.Clear();
			_usbPreviousPresentPaths.UnionWith(other);
			HashSet<string> testedPaths = (from port in _usbPorts
				where port.Passed && !string.IsNullOrWhiteSpace(port.LocationPath)
				select port.LocationPath).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool flag = false;
			foreach (UsbPortObservation item2 in list.Where((UsbPortObservation item) => !testedPaths.Contains(item.Path)))
			{
				UsbPortCache? usbPortCache = _usbPorts.FirstOrDefault((UsbPortCache port) => !port.Passed && !port.Failed) ?? _usbPorts.FirstOrDefault((UsbPortCache port) => !port.Passed);
				if (usbPortCache != null)
				{
					usbPortCache.Passed = true;
					usbPortCache.Failed = false;
					usbPortCache.LocationPath = item2.Path;
					usbPortCache.DeviceName = item2.Name;
					testedPaths.Add(item2.Path);
					flag = true;
					AddActivity("USB", $"{usbPortCache.Label} passed with {item2.Name}; upstream topology {item2.Path}.");
					continue;
				}
				break;
			}
			if (_usbPorts.All((UsbPortCache port) => port.Passed) && (!_usbPortTestFinished || _states.GetValueOrDefault("UsbPorts", "Waiting") != "Ok"))
			{
				_usbPortTestFinished = true;
				_states["UsbPorts"] = "Ok";
				AddActivity("USB", $"All {_usbPorts.Count} detected USB ports passed.");
				CheckForQaCompletionCelebration();
				flag = true;
			}
			else if (_usbPortTestFinished)
			{
				_states["UsbPorts"] = (_usbPorts.Any((UsbPortCache port) => port.Failed) ? "Bad" : "Ok");
			}
			UpdateUsbPortUi();
			if (flag)
			{
				SaveQaSessionCache();
			}
		}
		catch (Exception ex)
		{
			AddActivity("USB", "USB port scan failed: " + ex.Message);
		}
		finally
		{
			_usbPortScanRunning = false;
		}
	}

	private async Task<List<UsbPortObservation>> GetReadableUsbStorageAsync()
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(await PowerShellJsonAsync("$rows = @()\nGet-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue |\n    Where-Object { $_.InterfaceType -eq 'USB' -or $_.PNPDeviceID -like 'USBSTOR*' } |\n    ForEach-Object {\n        $disk = $_\n        $letters = @()\n        Get-CimAssociatedInstance -InputObject $disk -Association Win32_DiskDriveToDiskPartition -ErrorAction SilentlyContinue |\n            ForEach-Object {\n                Get-CimAssociatedInstance -InputObject $_ -Association Win32_LogicalDiskToPartition -ErrorAction SilentlyContinue\n            } |\n            ForEach-Object {\n                if ($_.DeviceID) { $letters += $_.DeviceID }\n            }\n        $readable = @($letters | Where-Object { Test-Path ($_.TrimEnd(':') + ':\\') }).Count -gt 0\n        if (-not $readable) { return }\n        $parentId = (Get-PnpDeviceProperty -InstanceId $disk.PNPDeviceID -KeyName 'DEVPKEY_Device_Parent' -ErrorAction SilentlyContinue).Data\n        $paths = if ($parentId) {\n            (Get-PnpDeviceProperty -InstanceId $parentId -KeyName 'DEVPKEY_Device_LocationPaths' -ErrorAction SilentlyContinue).Data\n        }\n        $path = @($paths | Where-Object { $_ -match 'USBROOT' } | Select-Object -First 1)[0]\n        if ($path) {\n            $rows += [pscustomobject]@{\n                Name = [string]$disk.Model\n                Path = [string]$path\n            }\n        }\n    }\n@($rows) | ConvertTo-Json -Compress"));
		return (from @group in (from record in (jsonDocument.RootElement.ValueKind == JsonValueKind.Array) ? jsonDocument.RootElement.EnumerateArray().ToArray() : ((jsonDocument.RootElement.ValueKind != JsonValueKind.Object) ? ((IEnumerable<JsonElement>)Array.Empty<JsonElement>()) : ((IEnumerable<JsonElement>)new JsonElement[1] { jsonDocument.RootElement }))
				select new UsbPortObservation(record.TryGetProperty("Name", out var value) ? (value.GetString() ?? "USB storage device") : "USB storage device", CanonicalizeUsbUpstreamPath(record.TryGetProperty("Path", out var value2) ? (value2.GetString() ?? "") : "")) into item
				where !string.IsNullOrWhiteSpace(item.Path)
				select item).GroupBy<UsbPortObservation, string>((UsbPortObservation item) => item.Path, StringComparer.OrdinalIgnoreCase)
			select @group.First()).ToList();
	}

	private async Task RefreshConnectedDockObservationsAsync()
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(await PowerShellJsonAsync("$rows = @()\nGet-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |\n    Where-Object {\n        $_.InstanceId -like 'USB\\*' -and\n        $_.Status -eq 'OK' -and\n        ($_.FriendlyName -match '(?i)(Dell.*Dock|Dell.*WD\\d+|Docking Station|USB.*Dock|Dock.*USB|WD19|WD22)' -or\n         $_.InstanceId -match '(?i)VID_413C&PID_B06E')\n    } |\n    ForEach-Object {\n        $paths = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_LocationPaths' -ErrorAction SilentlyContinue).Data\n        $path = @($paths | Where-Object { $_ -match 'USBROOT' } | Select-Object -First 1)[0]\n        if ($path) {\n            $rows += [pscustomobject]@{\n                Name = [string]$_.FriendlyName\n                Path = [string]$path\n            }\n        }\n    }\n@($rows) | ConvertTo-Json -Compress"));
			List<UsbPortObservation> list = (from @group in (from record in (jsonDocument.RootElement.ValueKind == JsonValueKind.Array) ? jsonDocument.RootElement.EnumerateArray().ToArray() : ((jsonDocument.RootElement.ValueKind != JsonValueKind.Object) ? ((IEnumerable<JsonElement>)Array.Empty<JsonElement>()) : ((IEnumerable<JsonElement>)new JsonElement[1] { jsonDocument.RootElement }))
					select new UsbPortObservation(record.TryGetProperty("Name", out var value) ? (value.GetString() ?? "USB docking station") : "USB docking station", CanonicalizeUsbUpstreamPath(record.TryGetProperty("Path", out var value2) ? (value2.GetString() ?? "") : "")) into item
					where !string.IsNullOrWhiteSpace(item.Path)
					select item).GroupBy<UsbPortObservation, string>((UsbPortObservation item) => item.Path, StringComparer.OrdinalIgnoreCase)
				select @group.FirstOrDefault((UsbPortObservation item) => item.Name.Contains("Dock", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(item.Name, "\\bWD\\d+", RegexOptions.IgnoreCase)) ?? @group.First()).ToList();
			_usbDockObservations.Clear();
			_usbDockObservations.AddRange(list);
			foreach (UsbPortObservation item in list)
			{
				AddActivity("USB", $"Connected dock available for USB port validation: {item.Name}; upstream topology {item.Path}.");
			}
		}
		catch (Exception ex)
		{
			AddActivity("USB", "Dock detection could not be refreshed: " + ex.Message);
		}
	}

	private static string CanonicalizeUsbUpstreamPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "";
		}
		return Regex.Replace(path.Trim(), "#USBMI\\([^)]+\\).*$", "", RegexOptions.IgnoreCase);
	}

	private UsbPortObservation NormalizeUsbObservationToConnectedDock(UsbPortObservation observation)
	{
		UsbPortObservation? usbPortObservation = _usbDockObservations.OrderByDescending((UsbPortObservation item) => item.Path.Length).FirstOrDefault((UsbPortObservation item) => observation.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) || observation.Path.StartsWith(item.Path + "#", StringComparison.OrdinalIgnoreCase));
		if (usbPortObservation is not null)
		{
			return new UsbPortObservation(usbPortObservation.Name, usbPortObservation.Path);
		}
		return observation;
	}

	private void FinalizePendingUsbPortsForQaSheet()
	{
		if (_usbPorts.Count == 0 || _usbPortTestFinished)
		{
			return;
		}
		int num = _usbPorts.Count((UsbPortCache port) => !port.Passed && !port.Failed);
		_usbPortTestFinished = true;
		foreach (UsbPortCache usbPort in _usbPorts)
		{
			if (!usbPort.Passed)
			{
				usbPort.Failed = true;
			}
		}
		int num2 = _usbPorts.Count((UsbPortCache port) => port.Failed);
		_states["UsbPorts"] = ((num2 > 0) ? "Bad" : "Ok");
		_usbPortTestActive = _qaLiveMonitoringActive && _usbPorts.Count > 0;
		if (_usbPortTestActive)
		{
			_usbPortPollTimer?.Start();
		}
		AddActivity("USB", (num > 0) ? $"QA Sheet selected with {num} untested USB port(s). USB result finalized: {_usbPorts.Count((UsbPortCache port) => port.Passed)} passed, {num2} failed." : $"QA Sheet selected. USB result finalized with all {_usbPorts.Count} detected ports passed.");
		UpdateUsbPortUi();
		SaveQaSessionCache();
		CheckForQaCompletionCelebration();
	}

	private void UpdateUsbPortUi()
	{
		if (UsbPortIndicatorsPanel == null)
		{
			return;
		}
		UsbPortIndicatorsPanel.Children.Clear();
		if (_usbPorts.Count == 0)
		{
			UsbPortIndicatorsPanel.Children.Add(CreateUsbPortPromptCard());
			return;
		}
		if (_usbPorts.Count != 0)
		{
			int num = Math.Min(6, _usbPorts.Count);
			int num2 = (int)Math.Ceiling((double)_usbPorts.Count / (double)num);
			double width = Math.Max(36.0, Math.Floor((348.0 - 4.0 * (double)Math.Max(0, num - 1) - 4.0) / (double)num));
			double height = Math.Max(15.0, Math.Floor((62.0 - 5.0 * (double)Math.Max(0, num2 - 1) - 4.0) / (double)num2));
			double fontSize = ((_usbPorts.Count <= 12) ? 10.5 : 8.5);
			for (int i = 0; i < _usbPorts.Count; i++)
			{
				UsbPortCache usbPortCache = _usbPorts[i];
				bool flag = i % num == num - 1 || i == _usbPorts.Count - 1;
				bool flag2 = i / num == num2 - 1;
				Brush foreground = (usbPortCache.Passed ? BrushFromHex("#55E3A4") : (usbPortCache.Failed ? BrushFromHex("#FF6B6B") : ((Brush)base.Resources["MutedBrush"])));
				Border border = new Border
				{
					Width = width,
					Height = height,
					Margin = new Thickness(0.0, 0.0, flag ? 0.0 : 4.0, flag2 ? 0.0 : 5.0),
					CornerRadius = new CornerRadius(8.0),
					BorderThickness = new Thickness(1.0),
					BorderBrush = (usbPortCache.Passed ? BrushFromHex("#55E3A4") : (usbPortCache.Failed ? BrushFromHex("#FF6B6B") : ((Brush)base.Resources["FinalCheckBorderBrush"]))),
					Background = (usbPortCache.Passed ? BrushFromHex("#332F855A") : (usbPortCache.Failed ? BrushFromHex("#338A4646") : ((Brush)base.Resources["FinalCheckUncheckedBrush"]))),
					ToolTip = (usbPortCache.Passed ? (usbPortCache.Label + " passed.") : (usbPortCache.Failed ? (usbPortCache.Label + " failed.") : (usbPortCache.Label + " has not been tested.")))
				};
				border.Child = new TextBlock
				{
					Text = usbPortCache.Label + " " + (usbPortCache.Passed ? "\u2713" : (usbPortCache.Failed ? "\u2715" : "\u2014")),
					Foreground = foreground,
					FontSize = fontSize,
					FontWeight = FontWeights.Bold,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				};
				UsbPortIndicatorsPanel.Children.Add(border);
			}
		}
	}

	private Border CreateUsbPortPromptCard()
	{
		bool liveMonitoringActive = _qaLiveMonitoringActive;
		string title = liveMonitoringActive ? "USB ports unavailable" : "Ready after reset";
		string detail = liveMonitoringActive
			? "Port count was not detected. Use QA Sheet to finalize if needed."
			: "Start New QA, then move a readable USB drive through each port.";
		string status = liveMonitoringActive ? "Review" : "Waiting";
		Brush accentBrush = liveMonitoringActive ? BrushFromHex("#FF9A9A") : (Brush)base.Resources["FinalCheckCheckedBoxBrush"];

		Border card = new Border
		{
			Width = 348.0,
			Height = 58.0,
			CornerRadius = new CornerRadius(12.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = accentBrush,
			Background = liveMonitoringActive ? BrushFromHex("#228A4646") : (Brush)base.Resources["FinalCheckUncheckedBrush"],
			Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
			ToolTip = detail
		};

		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62.0) });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		TextBlock titleBlock = new TextBlock
		{
			Text = title,
			Foreground = (Brush)base.Resources["TextBrush"],
			FontSize = 12.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetColumn(titleBlock, 0);
		Grid.SetRow(titleBlock, 0);
		grid.Children.Add(titleBlock);

		Border pill = new Border
		{
			CornerRadius = new CornerRadius(9.0),
			Background = accentBrush,
			Width = 54.0,
			Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		pill.Child = new TextBlock
		{
			Text = status,
			Foreground = liveMonitoringActive ? Brushes.White : BrushFromHex("#102A2D"),
			FontSize = 9.0,
			FontWeight = FontWeights.Bold,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		Grid.SetColumn(pill, 1);
		Grid.SetRow(pill, 0);
		grid.Children.Add(pill);

		TextBlock detailBlock = new TextBlock
		{
			Text = detail,
			Foreground = (Brush)base.Resources["MutedBrush"],
			FontSize = 9.6,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		Grid.SetColumn(detailBlock, 0);
		Grid.SetColumnSpan(detailBlock, 2);
		Grid.SetRow(detailBlock, 1);
		grid.Children.Add(detailBlock);

		card.Child = grid;
		return card;
	}

	#endregion

	#region Completion, power actions, drawers, and managed folders

	private void FinalCheck_Changed(object sender, RoutedEventArgs e)
	{
		if (sender is CheckBox checkBox)
		{
			AddActivity("Final Checks", $"{checkBox.Content} {((checkBox.IsChecked == true) ? "checked off" : "cleared")}.");
		}
		SaveQaSessionCache();
		CheckForQaCompletionCelebration();
	}

	private bool IsQaComplete()
	{
		bool num = new string[5] { "WiFi", "Ethernet", "Camera", "ExternalVideo", "Keyboard" }.All((string key) => IsFinalResult(_states.GetValueOrDefault(key, "Waiting")));
		string valueOrDefault = _states.GetValueOrDefault("Diagnostics", "Warning");
		bool flag = IsFinalResult(valueOrDefault) || (valueOrDefault == "Warning" && !DiagnosticsMain.Text.Contains("not found", StringComparison.OrdinalIgnoreCase) && !DiagnosticsMain.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
		bool flag2 = FinalTrackpadWorkingCheck.IsChecked == true && FinalHashGroupTagCheck.IsChecked == true && FinalDeletedUserCheck.IsChecked == true && FinalUpdateStockroomsCheck.IsChecked == true && FinalCleanedLaptopCheck.IsChecked == true && FinalConditionSuitableCheck.IsChecked == true;
		bool flag3 = _usbPorts.Count == 0 || (_usbPortTestFinished && _usbPorts.All((UsbPortCache port) => port.Passed || port.Failed));
		return num && flag && flag3 && flag2;
		static bool IsFinalResult(string state)
		{
			switch (state)
			{
			case "Ok":
			case "Bad":
			case "Ignored":
				return true;
			default:
				return false;
			}
		}
	}

	private void CheckForQaCompletionCelebration()
	{
		if (!_qaSessionReady || _suppressQaSessionCache)
		{
			return;
		}
		if (!IsQaComplete())
		{
			_completionCelebrated = false;
			if (SummaryStatus.Text == "QA Complete")
			{
				SetSummaryStatus("Ready");
			}
		}
		else if (!_completionCelebrated)
		{
			_completionCelebrated = true;
			SetSummaryStatus("QA Complete");
			AddActivity("QA", "All test sections and final checks are complete.");
			_ = PlayQaCompletionCelebrationAsync();
		}
	}

	private async Task PlayQaCompletionCelebrationAsync()
	{
		Grid? host = null;
		Canvas? overlay = null;
		try
		{
			if (!(Shell.Child is Grid grid))
			{
				return;
			}
			host = grid;
			overlay = new Canvas
			{
				Width = 1280.0,
				Height = 720.0,
				IsHitTestVisible = false,
				ClipToBounds = true,
				Opacity = 1.0
			};
			Panel.SetZIndex(overlay, 1000);
			host.Children.Add(overlay);
			Brush[] array = new Brush[6]
			{
				ResourceBrush("AccentBrush", "#A2E6DD"),
				new SolidColorBrush(Color.FromRgb(47, 180, 110)),
				new SolidColorBrush(Color.FromRgb(byte.MaxValue, 196, 76)),
				new SolidColorBrush(Color.FromRgb(70, 166, byte.MaxValue)),
				new SolidColorBrush(Color.FromRgb(245, 103, 169)),
				new SolidColorBrush(Color.FromRgb(164, 116, byte.MaxValue))
			};
			Random random = new Random((Environment.TickCount * 397) ^ _serviceTag.GetHashCode(StringComparison.OrdinalIgnoreCase));
			List<(Border Piece, double StartX, double StartY, double Drift, double Delay, double Duration, double StartAngle, double Spin, RotateTransform Rotate)> pieces = new List<(Border, double, double, double, double, double, double, double, RotateTransform)>();
			for (int i = 0; i < 72; i++)
			{
				RotateTransform rotateTransform = new RotateTransform(random.Next(-35, 36));
				Border border = new Border
				{
					Width = random.Next(8, 16),
					Height = random.Next(14, 25),
					CornerRadius = new CornerRadius(random.Next(1, 5)),
					Background = array[i % array.Length],
					Opacity = 0.0,
					RenderTransformOrigin = new Point(0.5, 0.5),
					RenderTransform = rotateTransform
				};
				int num = random.Next(18, 1262);
				int num2 = random.Next(-120, -12);
				Canvas.SetLeft(border, num);
				Canvas.SetTop(border, num2);
				overlay.Children.Add(border);
				pieces.Add((border, num, num2, random.Next(-145, 146), (double)random.Next(0, 420) / 1000.0, (double)random.Next(1750, 2450) / 1000.0, rotateTransform.Angle, random.Next(260, 900), rotateTransform));
			}
			ScaleTransform messageScale = new ScaleTransform(0.72, 0.72);
			Border message = new Border
			{
				Width = 440.0,
				Height = 118.0,
				CornerRadius = new CornerRadius(22.0),
				Background = ResourceBrush("ActivityPanelBrush", "#F5FFFFFF"),
				BorderBrush = ResourceBrush("AccentBrush", "#2F855A"),
				BorderThickness = new Thickness(2.5),
				Opacity = 0.0,
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = messageScale,
				Child = new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Children =
					{
						(UIElement)new TextBlock
						{
							Text = "QA COMPLETE",
							Foreground = ResourceBrush("TextBrush", "#12313A"),
							FontSize = 28.0,
							FontWeight = FontWeights.Bold,
							HorizontalAlignment = HorizontalAlignment.Center
						},
						(UIElement)new TextBlock
						{
							Text = "All sections and final checks are finished.",
							Foreground = ResourceBrush("MutedBrush", "#405A63"),
							FontSize = 12.5,
							Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
							HorizontalAlignment = HorizontalAlignment.Center
						}
					}
				}
			};
			Canvas.SetLeft(message, 420.0);
			Canvas.SetTop(message, 265.0);
			Panel.SetZIndex(message, 2);
			overlay.Children.Add(message);
			DateTime started = DateTime.UtcNow;
			while ((DateTime.UtcNow - started).TotalSeconds < 2.9)
			{
				double totalSeconds = (DateTime.UtcNow - started).TotalSeconds;
				foreach (var item in pieces)
				{
					if (!(totalSeconds < item.Delay))
					{
						double num3 = Math.Clamp((totalSeconds - item.Delay) / item.Duration, 0.0, 1.0);
						double num4 = num3 * num3;
						Canvas.SetTop(item.Piece, item.StartY + (820.0 - item.StartY) * num4);
						Canvas.SetLeft(item.Piece, item.StartX + item.Drift * num3 + Math.Sin(num3 * Math.PI * 5.0) * 16.0);
						item.Rest.Item2.Angle = item.StartAngle + item.Rest.Item1 * num3;
						item.Piece.Opacity = ((num3 < 0.8) ? 1.0 : Math.Max(0.0, (1.0 - num3) / 0.2));
					}
				}
				double num5 = Math.Clamp(totalSeconds / 0.42, 0.0, 1.0);
				double num6 = 0.72 + 0.28 * num5 + Math.Sin(num5 * Math.PI) * 0.07 * (1.0 - num5);
				double scaleX = (messageScale.ScaleY = num6);
				messageScale.ScaleX = scaleX;
				message.Opacity = ((totalSeconds < 2.5) ? num5 : Math.Max(0.0, (2.9 - totalSeconds) / 0.4));
				overlay.Opacity = ((totalSeconds < 2.5) ? 1.0 : Math.Max(0.0, (2.9 - totalSeconds) / 0.4));
				await Task.Delay(16);
			}
		}
		catch (Exception exception)
		{
			ErrorLog.WriteException("QA Celebration", "The optional completion animation was skipped. QA completion and saved results were preserved.", exception);
			AddActivity("QA", "Completion animation was skipped, but the QA remains complete and saved.");
		}
		finally
		{
			if (host != null && overlay != null)
			{
				host.Children.Remove(overlay);
			}
		}
		Brush ResourceBrush(string key, string fallback)
		{
			return (TryFindResource(key) as Brush) ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
		}
	}

	private void PowerButton_Click(object sender, RoutedEventArgs e)
	{
		PowerMenuPopup.IsOpen = true;
		AddActivity("Power", "Power menu opened.");
	}

	private async void ShutdownMenu_Click(object sender, RoutedEventArgs e)
	{
		await RunPowerActionAsync("Shutdown", () => ConfirmPowerAsync("Shut down this laptop now?", ShutdownExe, "/s /t 0"));
	}

	private async void RebootMenu_Click(object sender, RoutedEventArgs e)
	{
		await RunPowerActionAsync("Reboot", () => ConfirmPowerAsync("Reboot this laptop now?", ShutdownExe, "/r /t 0"));
	}

	private async void BiosMenu_Click(object sender, RoutedEventArgs e)
	{
		await RunPowerActionAsync("BIOS", RebootToBiosAsync);
	}

	private async void WinPeMenu_Click(object sender, RoutedEventArgs e)
	{
		await RunPowerActionAsync("Windows PE", RebootToWindowsPeAsync);
	}

	private async Task RunPowerActionAsync(string name, Func<Task> action)
	{
		try
		{
			PowerMenuPopup.IsOpen = false;
			AddActivity("Power", name + " request selected from header power menu.");
			await action();
		}
		catch (Exception ex)
		{
			AddActivity("Power", name + " request failed: " + ex.Message);
			MessageBox.Show(this, ex.Message, name + " request failed", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task ConfirmPowerAsync(string question, string exe, string args)
	{
		if (MessageBox.Show(this, question, "Power", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			RunExitCleanup(question);
			await RunProcessAsync(exe, args, 8);
			_closeCleanupComplete = true;
			Close();
		}
	}

	private async Task RebootToBiosAsync()
	{
		try
		{
			AddActivity("Power", "Direct BIOS setup reboot requested.");
			RunExitCleanup("BIOS setup reboot");
			await RunProcessAsync(ShutdownExe, "/r /fw /t 0", 8);
			_closeCleanupComplete = true;
			Close();
			return;
		}
		catch (Exception ex) when (ex.Message.Contains("203", StringComparison.OrdinalIgnoreCase))
		{
			AddActivity("Power", "Direct BIOS setup reboot was not accepted by Windows. Offering recovery-options fallback.");
		}
		if (MessageBox.Show(this, "Windows could not send this laptop directly into BIOS setup.\n\nReboot to recovery options instead? From there, choose UEFI Firmware Settings if it appears.", "BIOS setup", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
		{
			AddActivity("Power", "Recovery-options reboot requested after direct BIOS setup reboot was unavailable.");
			RunExitCleanup("BIOS recovery-options fallback reboot");
			await RunProcessAsync(ShutdownExe, "/r /o /t 0", 8);
			_closeCleanupComplete = true;
			Close();
		}
	}

	private async Task RebootToWindowsPeAsync()
	{
		if (MessageBox.Show(this, "Reboot to Windows recovery options now?", "Windows PE / Recovery", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			AddActivity("Power", "Windows recovery-options reboot requested.");
			RunExitCleanup("Windows recovery reboot");
			await RunProcessAsync(ShutdownExe, "/r /o /t 0", 8);
			_closeCleanupComplete = true;
			Close();
		}
	}

	private void ActivityDrawerButton_Click(object sender, RoutedEventArgs e)
	{
		_activityOpen = !_activityOpen;
		UpdateDrawerLayout();
		AddActivity("Activity", _activityOpen ? "Activity drawer shown." : "Activity drawer hidden.");
	}

	private void NotesDrawerButton_Click(object sender, RoutedEventArgs e)
	{
		_notesOpen = !_notesOpen;
		UpdateDrawerLayout();
		AddActivity("Notes", _notesOpen ? "Notes drawer shown." : "Notes drawer hidden.");
	}

	private void FoldersDrawerButton_Click(object sender, RoutedEventArgs e)
	{
		_foldersOpen = !_foldersOpen;
		UpdateDrawerLayout();
		AddActivity("Folders", _foldersOpen ? "Folders drawer shown." : "Folders drawer hidden.");
	}

	private void NotesCloseButton_Click(object sender, RoutedEventArgs e)
	{
		_notesOpen = false;
		UpdateDrawerLayout();
		AddActivity("Notes", "Notes drawer hidden.");
	}

	private void ActivityCloseButton_Click(object sender, RoutedEventArgs e)
	{
		_activityOpen = false;
		UpdateDrawerLayout();
		AddActivity("Activity", "Activity drawer hidden.");
	}

	private void HardwareCloseButton_Click(object sender, RoutedEventArgs e)
	{
		_hardwareOpen = false;
		UpdateDrawerLayout();
		AddActivity("Hardware", "Hardware drawer hidden.");
	}

	private void FoldersCloseButton_Click(object sender, RoutedEventArgs e)
	{
		_foldersOpen = false;
		UpdateDrawerLayout();
		AddActivity("Folders", "Folders drawer hidden.");
	}

	private void OpenQaSheetsFolderButton_Click(object sender, RoutedEventArgs e)
	{
		OpenManagedFolder(QaDir, "QA Sheets");
	}

	private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
	{
		OpenManagedFolder(LogsDir, "Logs");
	}

	private string CachedFileIdentifier(QaSessionCache? cache = null)
	{
		cache ??= ReadQaSessionCache();
		HardwareSnapshot hardware = cache?.Hardware ?? _hardware;
		string identifier = PreferredQaComputerName(
			hardware.Computer,
			cache?.ServiceTag ?? _serviceTag,
			hardware.BiosSerialNumber,
			hardware.ChassisSerial,
			cache?.AssetTag ?? _assetTag);
		return SafeFile(identifier, "Laptop");
	}

	private string CachedQaComputerName(QaSessionCache cache)
	{
		HardwareSnapshot hardware = cache.Hardware ?? new HardwareSnapshot();
		string identifier = PreferredQaComputerName(hardware.Computer, cache.ServiceTag, hardware.BiosSerialNumber, hardware.ChassisSerial, cache.AssetTag);
		return SafeFile(identifier, "Laptop");
	}

	private string PreferredQaComputerName(string? windowsComputerName, string? serviceTag, string? biosSerialNumber, string? chassisSerial, string? assetTag = null)
	{
		string serial = new[] { serviceTag, biosSerialNumber, chassisSerial }.FirstOrDefault(IsUsefulFileIdentifier)?.Trim() ?? "";
		string computer = IsUsefulFileIdentifier(windowsComputerName) && !IsGenericWindowsComputerName(windowsComputerName)
			? windowsComputerName!.Trim()
			: serial;
		string asset = IsUsefulFileIdentifier(assetTag) ? assetTag!.Trim() : "";
		string format = string.IsNullOrWhiteSpace(_config.QaComputerNameFormat) ? "LNG-{serial}" : _config.QaComputerNameFormat.Trim();
		string resolved = format
			.Replace("{serial}", serial, StringComparison.OrdinalIgnoreCase)
			.Replace("{computer}", computer, StringComparison.OrdinalIgnoreCase)
			.Replace("{asset}", asset, StringComparison.OrdinalIgnoreCase)
			.Trim();
		return IsUsefulFileIdentifier(resolved) ? resolved : new[] { computer, serial, asset }.FirstOrDefault(IsUsefulFileIdentifier)?.Trim() ?? "Laptop";
	}

	private static bool IsGenericWindowsComputerName(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), "^(?:DESKTOP|WIN|MININT)-[A-Z0-9]+$", RegexOptions.IgnoreCase);
	}


	private static bool IsUsefulFileIdentifier(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		return !Regex.IsMatch(value.Trim(), "^(?:unknown|unavailable|not set|n/?a|none)$", RegexOptions.IgnoreCase);
	}

	private void OpenActivityFolderButton_Click(object sender, RoutedEventArgs e)
	{
		OpenManagedFolder(ActivityDir, "Activity");
	}

	private void OpenHashFolderButton_Click(object sender, RoutedEventArgs e)
	{
		OpenManagedFolder(HashDir, "Hash");
	}

	private void OpenHardwareFolderButton_Click(object sender, RoutedEventArgs e)
	{
		OpenManagedFolder(HardwareDir, "Hardware");
	}

	private string ConfiguredCameraRollPath()
	{
		return Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(_config.CameraRoll) ? "C:\\Users\\defaultuser0\\Pictures\\Camera Roll" : _config.CameraRoll.Trim());
	}

	private void OpenCameraRollFolderButton_Click(object sender, RoutedEventArgs e)
	{
		string text = ConfiguredCameraRollPath();
		if (Directory.Exists(text))
		{
			OpenManagedFolder(text, "Camera Roll");
			return;
		}
		AddActivity("Folders", "Camera Roll folder open failed: configured location was not found: " + text);
		MessageBox.Show(this, "The configured Camera Roll folder was not found:\n" + text, "Folders", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	#endregion

	#region Drawer layout, QA session persistence, settings, and window commands

	private void OpenDiagnosticsFolderButton_Click(object sender, RoutedEventArgs e)
	{
		string text = FindDiagnosticsBrowseStartFolder();
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			OpenManagedFolder(text, "Diagnostics");
			return;
		}
		AddActivity("Folders", "Diagnostics folder open failed: no FAT32 diagnostics drive was detected.");
		MessageBox.Show(this, "No FAT32 diagnostics drive was detected.", "Folders", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void OpenManagedFolder(string path, string label)
	{
		try
		{
			EnsureFolders();
			Process.Start(new ProcessStartInfo(path)
			{
				UseShellExecute = true
			});
			AddActivity("Folders", label + " folder opened: " + path);
		}
		catch (Exception ex)
		{
			AddActivity("Folders", label + " folder open failed: " + ex.Message);
			MessageBox.Show(this, label + " folder could not be opened:\n" + ex.Message, "Folders", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void UpdateDrawerLayout()
	{
		SyncDrawerOrder("Notes", _notesOpen);
		SyncDrawerOrder("Activity", _activityOpen);
		SyncDrawerOrder("Hardware", _hardwareOpen);
		SyncDrawerOrder("Folders", _foldersOpen);
		double num = ((_drawerOrder.Count <= 1) ? 396.0 : Math.Min(396.0, 776.0 / (double)(_drawerOrder.Count - 1)));
		for (int i = 0; i < _drawerOrder.Count; i++)
		{
			double to = 830.0 - (double)(_drawerOrder.Count - 1 - i) * num;
			Border border = DrawerPanel(_drawerOrder[i]);
			Panel.SetZIndex(border, 10 + i);
			border.BeginAnimation(Canvas.LeftProperty, null);
			border.Visibility = Visibility.Visible;
			border.Opacity = 1.0;
			StartLayoutAnimation(border, Canvas.LeftProperty, to);
		}
		CloseDrawerIfNeeded("Notes", _notesOpen, 1260.0);
		CloseDrawerIfNeeded("Activity", _activityOpen, 1260.0);
		CloseDrawerIfNeeded("Hardware", _hardwareOpen, 1260.0);
		CloseDrawerIfNeeded("Folders", _foldersOpen, 1260.0);
		NotesDrawerButton.ToolTip = (_notesOpen ? "Hide notes" : "Show notes");
		ActivityDrawerButton.ToolTip = (_activityOpen ? "Hide activity" : "Show activity");
		HardwareButton.ToolTip = (_hardwareOpen ? "Hide hardware snapshot" : "Show hardware snapshot");
		FoldersDrawerButton.ToolTip = (_foldersOpen ? "Hide folders" : "Show folders");
		UpdateDrawerTabBorders();
	}

	private void UpdateDrawerTabBorders()
	{
		SetDrawerTabBorder(NotesDrawerButton, _notesOpen);
		SetDrawerTabBorder(ActivityDrawerButton, _activityOpen);
		SetDrawerTabBorder(HardwareButton, _hardwareOpen);
		SetDrawerTabBorder(FoldersDrawerButton, _foldersOpen);
	}

	private void SetDrawerTabBorder(Button tab, bool isOpen)
	{
		tab.BorderThickness = (isOpen ? new Thickness(2.4) : new Thickness(0.0));
		tab.BorderBrush = (isOpen ? BrushFromHex((_currentTheme == "Light") ? "#5F9EA8" : ((_currentTheme == "AMOLED") ? "#D0D0D0" : "#8FB8C1")) : Brushes.Transparent);
	}

	private void SyncDrawerOrder(string drawer, bool isOpen)
	{
		if (isOpen)
		{
			if (!_drawerOrder.Contains(drawer))
			{
				_drawerOrder.Add(drawer);
			}
		}
		else
		{
			_drawerOrder.Remove(drawer);
		}
	}

	private void CloseDrawerIfNeeded(string drawer, bool isOpen, double closedLeft)
	{
		if (isOpen)
		{
			return;
		}
		Border panel = DrawerPanel(drawer);
		if (panel.Visibility != Visibility.Visible)
		{
			return;
		}
		StartLayoutAnimation(panel, Canvas.LeftProperty, closedLeft, delegate
		{
			if (!DrawerIsOpen(drawer))
			{
				panel.Visibility = Visibility.Collapsed;
				panel.Opacity = 1.0;
			}
		});
	}

	private Border DrawerPanel(string drawer)
	{
		return drawer switch
		{
			"Notes" => SheetNotesPanel,
			"Activity" => ActivityPanel,
			"Hardware" => HardwarePanel,
			"Folders" => FoldersPanel,
			_ => throw new ArgumentOutOfRangeException("drawer", drawer, null),
		};
	}

	private bool DrawerIsOpen(string drawer)
	{
		return drawer switch
		{
			"Notes" => _notesOpen,
			"Activity" => _activityOpen,
			"Hardware" => _hardwareOpen,
			"Folders" => _foldersOpen,
			_ => false,
		};
	}

	private static void StartLayoutAnimation(FrameworkElement target, DependencyProperty property, double to, Action? completed = null)
	{
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			To = to,
			Duration = TimeSpan.FromMilliseconds(220L),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		if (completed != null)
		{
			doubleAnimation.Completed += delegate
			{
				completed();
			};
		}
		target.BeginAnimation(property, doubleAnimation);
	}

	private void ActivityCopyButton_Click(object sender, RoutedEventArgs e)
	{
		Clipboard.SetText(ActivityBox.Text);
		AddActivity("Activity", "Activity copied to clipboard.");
	}

	private void ActivitySaveButton_Click(object sender, RoutedEventArgs e)
	{
		EnsureFolders();
		CleanupOldFiles(ActivityDir, 90, "Activity", "activity log file(s)", recursive: true);
		ErrorLog.StartSession(CachedFileIdentifier());
		AddActivity("Activity", "Activity log is already being saved automatically for this app session.");
		string text = ErrorLog.ActivityLogPath;
		MessageBox.Show(this, "Activity is saved automatically in the single log for this app session:\n" + text, "Activity", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private string SaveHardwareSnapshotForNewQa()
	{
		try
		{
			EnsureFolders();
			CleanupOldFiles(HardwareDir, 90, "Hardware", "hardware snapshot(s)");
			string value = CachedFileIdentifier();
			string text = Path.Combine(HardwareDir, $"{value}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-Hardware.txt");
			File.WriteAllText(text, HardwareDetailText());
			AddActivity("Hardware", "New QA hardware snapshot saved: " + text);
			return text;
		}
		catch (Exception ex)
		{
			AddActivity("Hardware", "New QA hardware snapshot save failed: " + ex.Message);
			return "";
		}
	}

	private QaSessionCache? ReadQaSessionCache()
	{
		if (!File.Exists(QaSessionCachePath))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(QaSessionCachePath));
		}
		catch (Exception ex)
		{
			AddActivity("System", "Cached QA session read failed: " + ex.Message);
			return null;
		}
	}

	private static void NormalizeQaSessionIdentity(QaSessionCache cache)
	{
		if (string.IsNullOrWhiteSpace(cache.SessionId))
		{
			cache.SessionId = Guid.NewGuid().ToString("N");
		}
		if (cache.StartedAt == default)
		{
			cache.StartedAt = cache.SavedAt == default ? DateTime.Now : cache.SavedAt;
		}
	}

	private void EnsureQaSessionIdentity(QaSessionCache cache)
	{
		NormalizeQaSessionIdentity(cache);
		_activeQaSessionId = cache.SessionId;
		_activeQaSessionStartedAt = cache.StartedAt;
	}

	private void EnsureActiveQaSessionIdentity()
	{
		if (string.IsNullOrWhiteSpace(_activeQaSessionId))
		{
			_activeQaSessionId = Guid.NewGuid().ToString("N");
		}
		if (_activeQaSessionStartedAt == default)
		{
			_activeQaSessionStartedAt = DateTime.Now;
		}
	}

	private string QaSessionArchivePath(string sessionId)
	{
		return Path.Combine(QaSessionArchiveDir, "session-" + sessionId + ".json");
	}

	private static void WriteJsonAtomic(string path, string contents)
	{
		string tempPath = path + $".{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(tempPath, contents, Encoding.UTF8);
			File.Move(tempPath, path, overwrite: true);
		}
		finally
		{
			try
			{
				if (File.Exists(tempPath))
				{
					File.Delete(tempPath);
				}
			}
			catch
			{
			}
		}
	}

	private void CleanupCachedSessions()
	{
		if (!Directory.Exists(QaSessionArchiveDir))
		{
			return;
		}
		DateTime cutoff = DateTime.Now.AddDays(-QaAndDiagnosticsRetentionDays);
		int removed = 0;
		foreach (string path in Directory.GetFiles(QaSessionArchiveDir, "session-*.json", SearchOption.TopDirectoryOnly))
		{
			try
			{
				QaSessionCache? cache = JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(path));
				DateTime sessionDate = cache?.StartedAt != default ? cache!.StartedAt : (cache?.SavedAt != default ? cache!.SavedAt : File.GetLastWriteTime(path));
				if (sessionDate < cutoff)
				{
					File.Delete(path);
					removed++;
				}
			}
			catch
			{
				if (File.GetLastWriteTime(path) < cutoff)
				{
					File.Delete(path);
					removed++;
				}
			}
		}
		if (removed > 0)
		{
			AddActivity("Sessions", $"Removed {removed} cached QA session(s) older than {QaAndDiagnosticsRetentionDays} days.");
		}
		WriteCachedSessionIndex();
	}

	private void WriteCachedSessionIndex()
	{
		try
		{
			Directory.CreateDirectory(QaSessionArchiveDir);
			List<CachedSessionIndexEntry> entries = new List<CachedSessionIndexEntry>();
			foreach (string path in Directory.GetFiles(QaSessionArchiveDir, "session-*.json", SearchOption.TopDirectoryOnly))
			{
				try
				{
					QaSessionCache? cache = JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(path));
					if (cache == null)
					{
						continue;
					}
					NormalizeQaSessionIdentity(cache);
					entries.Add(new CachedSessionIndexEntry
					{
						SessionId = cache.SessionId,
						FileName = Path.GetFileName(path),
						ServiceTag = CachedSessionSerial(cache),
						StartedAt = cache.StartedAt,
						SavedAt = cache.SavedAt
					});
				}
				catch
				{
				}
			}
			entries = entries.OrderByDescending(entry => entry.StartedAt).ToList();
			WriteJsonAtomic(QaSessionIndexPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex)
		{
			AddActivity("Sessions", "Session index update skipped: " + ex.Message);
		}
	}

	private void PromoteLegacyQaSessionCache()
	{
		if (!File.Exists(QaSessionCachePath))
		{
			return;
		}
		try
		{
			QaSessionCache? cache = JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(QaSessionCachePath));
			if (cache == null)
			{
				return;
			}
			NormalizeQaSessionIdentity(cache);
			string archivePath = QaSessionArchivePath(cache.SessionId);
			if (!File.Exists(archivePath))
			{
				WriteJsonAtomic(archivePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
			}
			WriteCachedSessionIndex();
		}
		catch (Exception ex)
		{
			AddActivity("Sessions", "Legacy QA cache promotion skipped: " + ex.Message);
		}
	}

	private static string CachedSessionSerial(QaSessionCache cache)
	{
		string? serial = new[]
		{
			cache.ServiceTag,
			cache.Hardware?.BiosSerialNumber,
			cache.Hardware?.ChassisSerial,
			cache.AssetTag
		}.FirstOrDefault(IsUsefulFileIdentifier);
		return string.IsNullOrWhiteSpace(serial) ? "Unknown Serial" : serial.Trim();
	}

	private void RefreshCachedSessionPicker(string? preferredSessionId = null)
	{
		if (CachedSessionPicker == null)
		{
			return;
		}
		CleanupCachedSessions();
		Directory.CreateDirectory(QaSessionArchiveDir);
		PromoteLegacyQaSessionCache();
		_cachedSessionOptions.Clear();
		if (Directory.Exists(QaSessionArchiveDir))
		{
			foreach (string path in Directory.GetFiles(QaSessionArchiveDir, "session-*.json", SearchOption.TopDirectoryOnly))
			{
				try
				{
					QaSessionCache? cache = JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(path));
					if (cache == null)
					{
						continue;
					}
					NormalizeQaSessionIdentity(cache);
					DateTime sessionDate = cache.StartedAt == default ? cache.SavedAt : cache.StartedAt;
					string displayName = $"{CachedSessionSerial(cache)} - {sessionDate:g}";
					_cachedSessionOptions.Add(new CachedSessionOption(path, cache, displayName));
				}
				catch
				{
				}
			}
		}
		_cachedSessionOptions.Sort((left, right) => right.Session.StartedAt.CompareTo(left.Session.StartedAt));
		string sessionId = preferredSessionId ?? _activeQaSessionId;
		_updatingCachedSessionPicker = true;
		try
		{
			CachedSessionPicker.ItemsSource = _cachedSessionOptions.ToList();
			CachedSessionPicker.SelectedItem = _cachedSessionOptions.FirstOrDefault(option => string.Equals(option.Session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
			CachedSessionPicker.IsEnabled = _cachedSessionOptions.Count > 0;
			if (_cachedSessionOptions.Count == 0)
			{
				CachedSessionPicker.Text = "No cached sessions";
			}
		}
		finally
		{
			_updatingCachedSessionPicker = false;
		}
	}

	private void FilterCachedSessionPicker(string query)
	{
		string search = query.Trim();
		List<CachedSessionOption> matches = string.IsNullOrWhiteSpace(search)
			? _cachedSessionOptions.ToList()
			: _cachedSessionOptions.Where(option => option.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToList();
		_updatingCachedSessionPicker = true;
		try
		{
			CachedSessionPicker.ItemsSource = matches;
			CachedSessionPicker.SelectedItem = null;
			CachedSessionPicker.Text = query;
			CachedSessionPicker.IsDropDownOpen = true;
			if (CachedSessionPicker.Template.FindName("PART_EditableTextBox", CachedSessionPicker) is TextBox editor)
			{
				editor.CaretIndex = editor.Text.Length;
			}
		}
		finally
		{
			_updatingCachedSessionPicker = false;
		}
	}

	private void CachedSessionPicker_KeyUp(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			CachedSessionPicker.IsDropDownOpen = false;
			return;
		}
		if (e.Key == Key.Enter)
		{
			if (CachedSessionPicker.SelectedItem == null && CachedSessionPicker.Items.Count > 0)
			{
				CachedSessionPicker.SelectedIndex = 0;
			}
			CachedSessionPicker.IsDropDownOpen = false;
			return;
		}
		if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Tab)
		{
			return;
		}
		FilterCachedSessionPicker(CachedSessionPicker.Text ?? "");
	}

	private void CachedSessionPicker_DropDownOpened(object sender, EventArgs e)
	{
		if (_updatingCachedSessionPicker)
		{
			return;
		}
		CachedSessionPicker.ItemsSource = _cachedSessionOptions.ToList();
	}

	private void CachedSessionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_updatingCachedSessionPicker || CachedSessionPicker.SelectedItem is not CachedSessionOption option || string.Equals(option.Session.SessionId, _activeQaSessionId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		SaveQaSessionCache();
		try
		{
			QaSessionCache? cache = JsonSerializer.Deserialize<QaSessionCache>(File.ReadAllText(option.FilePath));
			if (cache == null)
			{
				throw new InvalidDataException("The cached session file is empty.");
			}
			EnsureQaSessionIdentity(cache);
			_startupDataRefreshRequired = false;
			RestoreQaSessionCache(cache);
			SaveQaSessionCache();
			_completionCelebrated = IsQaComplete();
			SetSummaryStatus(_completionCelebrated ? "QA Complete" : "Ready");
			RefreshCachedSessionPicker(cache.SessionId);
			AddActivity("Sessions", "Loaded cached QA session: " + option.DisplayName);
		}
		catch (Exception ex)
		{
			AddActivity("Sessions", "Cached QA session could not be loaded: " + ex.Message);
			MessageBox.Show(this, "The selected cached session could not be loaded.\n\n" + ex.Message, "Cached Sessions", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			RefreshCachedSessionPicker();
		}
	}

	private static bool ShouldSkipStartupRefresh(QaSessionCache cache)
	{
		if (!cache.StartupDataSaved && !HasCachedStartupData(cache))
		{
			return HasSavedQaProgress(cache);
		}
		return true;
	}

	private static bool HasSavedQaProgress(QaSessionCache cache)
	{
		if (cache.FinalHashGroupTag != true && cache.FinalCleanedLaptop != true && cache.FinalUpdateStockrooms != true && cache.FinalTrackpadWorking != true && cache.FinalDeletedUser != true && cache.FinalConditionSuitableForUse != true && !cache.UsbPortTestFinished)
		{
			List<UsbPortCache> usbPorts = cache.UsbPorts;
			if (usbPorts == null || !usbPorts.Any((UsbPortCache port) => port.Passed || port.Failed))
			{
				if (!string.IsNullOrWhiteSpace(cache.RmaIssues) || !string.IsNullOrWhiteSpace(cache.RepairNotes) || !string.IsNullOrWhiteSpace(cache.DiagnosticsLogPath) || !string.IsNullOrWhiteSpace(cache.DiagnosticsRawText))
				{
					return true;
				}
				if (cache.Steps == null)
				{
					return false;
				}
				return cache.Steps.Any<KeyValuePair<string, QaStepCache>>(delegate(KeyValuePair<string, QaStepCache> entry)
				{
					QaStepCache value = entry.Value;
					if (value == null)
					{
						return false;
					}
					return string.Equals(entry.Key, "Diagnostics", StringComparison.OrdinalIgnoreCase) ? string.Equals(value.State, "Ok", StringComparison.OrdinalIgnoreCase) : (!string.Equals(value.State, "Waiting", StringComparison.OrdinalIgnoreCase));
				});
			}
		}
		return true;
	}

	private static bool HasCachedStartupData(QaSessionCache cache)
	{
		if (string.IsNullOrWhiteSpace(cache.ServiceTag) && string.IsNullOrWhiteSpace(cache.AssetTag) && string.IsNullOrWhiteSpace(cache.Warranty) && string.IsNullOrWhiteSpace(cache.BatterySummary) && cache.CurrentBattery == null && string.IsNullOrWhiteSpace(cache.SecureBootState))
		{
			return cache.Hardware != null;
		}
		return true;
	}

	private bool HasCurrentStartupData()
	{
		if (string.IsNullOrWhiteSpace(_serviceTag) && string.IsNullOrWhiteSpace(_assetTag) && string.IsNullOrWhiteSpace(_warranty) && string.IsNullOrWhiteSpace(_batterySummary) && !_currentBattery.IsPresent && string.IsNullOrWhiteSpace(_states.GetValueOrDefault("SecureBoot", "")) && string.IsNullOrWhiteSpace(_hardware.Model))
		{
			return !string.IsNullOrWhiteSpace(_hardware.Computer);
		}
		return true;
	}

	private bool RestoreQaSessionCache()
	{
		QaSessionCache? qaSessionCache = ReadQaSessionCache();
		if (qaSessionCache != null)
		{
			return RestoreQaSessionCache(qaSessionCache);
		}
		return false;
	}

	private bool RestoreQaSessionCache(QaSessionCache cache)
	{
		if (_startupDataRefreshRequired)
		{
			AddActivity("System", "Ignored a shared QA cache update while fresh device data was being collected.");
			return false;
		}
		try
		{
			EnsureQaSessionIdentity(cache);
			if (cache.Steps == null)
			{
				Dictionary<string, QaStepCache> dictionary = (cache.Steps = new Dictionary<string, QaStepCache>());
			}
			if (!string.IsNullOrWhiteSpace(cache.DiagnosticsRawText))
			{
				DiagnosticsResult normalizedDiagnostics = ParseDiagnosticsLog(cache.DiagnosticsLogPath ?? "", cache.DiagnosticsRawText);
				cache.Steps["Diagnostics"] = new QaStepCache
				{
					State = normalizedDiagnostics.State,
					MainText = normalizedDiagnostics.MainText,
					DetailText = normalizedDiagnostics.DetailText
				};
			}
			_suppressQaSessionCache = true;
			RestoreCachedStartupData(cache);
			ErrorLog.StartSession(CachedFileIdentifier(cache));
			RestoreCachedStep(cache, "WiFi", WifiIcon, WifiMain, WifiDetail);
			RestoreCachedStep(cache, "Ethernet", EthernetIcon, EthernetMain, EthernetDetail);
			RestoreCachedStep(cache, "Camera", CameraIcon, CameraMain, CameraDetail);
			RestoreCachedStep(cache, "ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail);
			RestoreCachedStep(cache, "Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail);
			RestoreCachedStep(cache, "Diagnostics", DiagnosticsIcon, DiagnosticsMain, DiagnosticsDetail);
			FinalHashGroupTagCheck.IsChecked = cache.FinalHashGroupTag;
			FinalCleanedLaptopCheck.IsChecked = cache.FinalCleanedLaptop;
			FinalUpdateStockroomsCheck.IsChecked = cache.FinalUpdateStockrooms;
			FinalTrackpadWorkingCheck.IsChecked = cache.FinalTrackpadWorking;
			FinalDeletedUserCheck.IsChecked = cache.FinalDeletedUser;
			FinalConditionSuitableCheck.IsChecked = cache.FinalConditionSuitableForUse;
			_usbPortTestFinished = cache.UsbPortTestFinished;
			List<UsbPortCache> usbPorts = cache.UsbPorts;
			if (usbPorts != null && usbPorts.Count > 0)
			{
				if (_usbPorts.Count == 0)
				{
					_usbPorts.AddRange(cache.UsbPorts.Select((UsbPortCache port, int index) => new UsbPortCache
					{
						Label = $"USB {index + 1}",
						Passed = port.Passed,
						Failed = port.Failed,
						LocationPath = port.LocationPath,
						DeviceName = port.DeviceName
					}));
				}
				else
				{
					for (int num = 0; num < Math.Min(_usbPorts.Count, cache.UsbPorts.Count); num++)
					{
						UsbPortCache usbPortCache = cache.UsbPorts[num];
						_usbPorts[num].Passed = usbPortCache.Passed;
						_usbPorts[num].Failed = usbPortCache.Failed;
						_usbPorts[num].LocationPath = usbPortCache.LocationPath;
						_usbPorts[num].DeviceName = usbPortCache.DeviceName;
					}
				}
			}
			if (_usbPorts.Count > 0 && _usbPorts.All((UsbPortCache port) => port.Passed))
			{
				_usbPortTestFinished = true;
			}
			if (_usbPorts.Any((UsbPortCache port) => !port.Passed && !port.Failed))
			{
				_usbPortTestFinished = false;
			}
			_states["UsbPorts"] = ((!_usbPortTestFinished) ? "Working" : (_usbPorts.Any((UsbPortCache port) => port.Failed) ? "Bad" : "Ok"));
			_usbPortTestActive = _qaLiveMonitoringActive && _usbPorts.Count > 0;
			if (_usbPortTestActive)
			{
				_usbPortPollTimer?.Start();
			}
			else
			{
				_usbPortPollTimer?.Stop();
			}
			UpdateUsbPortUi();
			RmaIssueBox.Text = cache.RmaIssues ?? "";
			RepairNotesBox.Text = cache.RepairNotes ?? "";
			_diagnosticsLogPath = cache.DiagnosticsLogPath ?? "";
			_diagnosticsRawText = cache.DiagnosticsRawText ?? "";
			DiagnosticsRawButton.IsEnabled = !string.IsNullOrWhiteSpace(_diagnosticsRawText);
			_suppressQaSessionCache = false;
			AddActivity("System", "Cached QA session restored.");
			if (File.Exists(QaSessionCachePath))
			{
				_qaSessionCacheWriteUtc = File.GetLastWriteTimeUtc(QaSessionCachePath);
			}
			return true;
		}
		catch (Exception ex)
		{
			_suppressQaSessionCache = false;
			AddActivity("System", "Cached QA session restore failed: " + ex.Message);
			return false;
		}
	}

	private void RestoreCachedStartupData(QaSessionCache cache)
	{
		if (!HasCachedStartupData(cache))
		{
			return;
		}
		if (!string.IsNullOrWhiteSpace(cache.ServiceTag))
		{
			_serviceTag = cache.ServiceTag.Trim();
			HeaderSerial.Text = L("Service Tag: " + _serviceTag);
		}
		if (!string.IsNullOrWhiteSpace(cache.AssetTag))
		{
			_assetTag = cache.AssetTag.Trim();
			UpdateAssetHeader();
		}
		if (cache.Warranty != null)
		{
			_warranty = cache.Warranty;
			HeaderWarranty.Text = L("Warranty: " + WarrantyDisplayText());
			HeaderWarranty.ToolTip = WarrantyToolTipText();
		}
		if (!string.IsNullOrWhiteSpace(cache.WarrantyCachedServiceTag))
		{
			_warrantyCachedServiceTag = cache.WarrantyCachedServiceTag.Trim();
		}
		if (!string.IsNullOrWhiteSpace(cache.BatterySummary))
		{
			_batteryHealthRating = NormalizeBatteryHealthRating(cache.BatteryHealthRating);
			if (string.IsNullOrWhiteSpace(_batteryHealthRating))
			{
				_batteryHealthRating = BatteryHealthRatingFromDiagnostics(cache.DiagnosticsRawText);
			}
			_batterySummary = BatteryHealthSummary(cache.BatterySummary.Trim(), _batteryHealthRating);
			UpdateBatteryHealthDisplay();
		}
		if (cache.Hardware != null)
		{
			_hardware = cache.Hardware;
			_hardware.Computer = PreferredQaComputerName(_hardware.Computer, cache.ServiceTag, _hardware.BiosSerialNumber, _hardware.ChassisSerial, cache.AssetTag);
			UpdateDeviceNameHeader();
			if (_hardwareOpen)
			{
				HardwareDetailsBox.Text = HardwareDetailText();
			}
		}
		if (!string.IsNullOrWhiteSpace(cache.SecureBootState))
		{
			_states["SecureBoot"] = cache.SecureBootState.Trim();
			SetBiosButtonState(BiosSecureBootButton, _states["SecureBoot"], "Secure Boot");
			SetBiosStatusIcon(_states["SecureBoot"]);
		}
		if (!string.IsNullOrWhiteSpace(cache.BiosStatusText))
		{
			BiosStatusText.Text = cache.BiosStatusText;
		}
		else if (!string.IsNullOrWhiteSpace(cache.SecureBootState))
		{
			BiosStatusText.Text = "Secure Boot " + StatePhrase(_states["SecureBoot"], "on", "off") + ".";
		}
	}

	private void RestoreCachedStep(QaSessionCache cache, string key, TextBlock icon, TextBox main, TextBox detail)
	{
		if (cache.Steps.TryGetValue(key, out QaStepCache? value))
		{
			SetStep(key, icon, main, detail, ValueOrFallback(value.State, "Waiting"), ValueOrFallback(value.MainText, main.Text), ValueOrFallback(value.DetailText, detail.Text));
		}
	}

	private void SaveQaSessionCache()
	{
		if (_suppressQaSessionCache)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(RuntimeDir);
			Directory.CreateDirectory(QaSessionArchiveDir);
			EnsureActiveQaSessionIdentity();
			QaSessionCache qaSessionCache = new QaSessionCache
			{
				SessionId = _activeQaSessionId,
				StartedAt = _activeQaSessionStartedAt,
				SavedAt = DateTime.Now,
				StartupDataSaved = (!_startupDataRefreshRequired && HasCurrentStartupData()),
				FinalHashGroupTag = FinalHashGroupTagCheck.IsChecked,
				FinalCleanedLaptop = FinalCleanedLaptopCheck.IsChecked,
				FinalUpdateStockrooms = FinalUpdateStockroomsCheck.IsChecked,
				FinalTrackpadWorking = FinalTrackpadWorkingCheck.IsChecked,
				FinalDeletedUser = FinalDeletedUserCheck.IsChecked,
				FinalConditionSuitableForUse = FinalConditionSuitableCheck.IsChecked,
				UsbPortTestFinished = _usbPortTestFinished,
				UsbPorts = _usbPorts.Select((UsbPortCache port) => new UsbPortCache
				{
					Label = port.Label,
					Passed = port.Passed,
					Failed = port.Failed,
					LocationPath = port.LocationPath,
					DeviceName = port.DeviceName
				}).ToList(),
				RmaIssues = RmaIssueBox.Text,
				RepairNotes = RepairNotesBox.Text,
				DiagnosticsLogPath = _diagnosticsLogPath,
				DiagnosticsRawText = _diagnosticsRawText
			};
			if (qaSessionCache.StartupDataSaved)
			{
				qaSessionCache.ServiceTag = _serviceTag;
				qaSessionCache.AssetTag = _assetTag;
				qaSessionCache.Warranty = _warranty;
				qaSessionCache.WarrantyCachedServiceTag = _warrantyCachedServiceTag;
				qaSessionCache.BatterySummary = _batterySummary;
				qaSessionCache.BatteryHealthRating = _batteryHealthRating;
				qaSessionCache.CurrentBattery = _currentBattery;
				qaSessionCache.Hardware = _hardware;
				qaSessionCache.SecureBootState = _states.GetValueOrDefault("SecureBoot", "Unknown");
				qaSessionCache.BiosStatusText = BiosStatusText.Text;
			}
			qaSessionCache.Steps["WiFi"] = CaptureStep("WiFi", WifiMain, WifiDetail);
			qaSessionCache.Steps["Ethernet"] = CaptureStep("Ethernet", EthernetMain, EthernetDetail);
			qaSessionCache.Steps["Camera"] = CaptureStep("Camera", CameraMain, CameraDetail);
			qaSessionCache.Steps["ExternalVideo"] = CaptureStep("ExternalVideo", ExternalMain, ExternalDetail);
			qaSessionCache.Steps["Keyboard"] = CaptureStep("Keyboard", KeyboardMain, KeyboardDetail);
			qaSessionCache.Steps["Diagnostics"] = CaptureStep("Diagnostics", DiagnosticsMain, DiagnosticsDetail);
			string contents = JsonSerializer.Serialize(qaSessionCache, new JsonSerializerOptions
			{
				WriteIndented = true
			});
		WriteJsonAtomic(QaSessionCachePath, contents);
			WriteJsonAtomic(QaSessionArchivePath(qaSessionCache.SessionId), contents);
			WriteCachedSessionIndex();
			_qaSessionCacheWriteUtc = File.GetLastWriteTimeUtc(QaSessionCachePath);
		}
		catch
		{
		}
	}

	private QaStepCache CaptureStep(string key, TextBox main, TextBox detail)
	{
		return new QaStepCache
		{
			State = _states.GetValueOrDefault(key, "Waiting"),
			MainText = main.Text,
			DetailText = detail.Text
		};
	}

	private void QaSessionInput_Changed(object sender, TextChangedEventArgs e)
	{
		ScheduleQaSessionCacheSave();
	}

	private void AnyAppInteraction_Changed(object sender, RoutedEventArgs e)
	{
		ScheduleQaSessionCacheSave();
	}

	private void ScheduleQaSessionCacheSave()
	{
		if (_suppressQaSessionCache || !_qaSessionReady)
		{
			return;
		}

		_qaSessionSaveTimer.Stop();
		_qaSessionSaveTimer.Start();
	}

	private void MainWindow_Activated(object? sender, EventArgs e)
	{
		WpfLocalization.Apply(this, _config.AppLanguage);
		if (_qaSessionReady)
		{
			_ = RefreshCurrentBatteryAsync();
		}
		if (File.Exists(ConfigPath))
		{
			try
			{
				DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(ConfigPath);
				if (lastWriteTimeUtc > _configWriteUtc)
				{
					_config = LoadConfig();
					LanguageCatalog.ApplyCulture(_config.AppLanguage);
					ApplyAppTheme(_config.AppTheme);
					WpfLocalization.Apply(this, _config.AppLanguage);
					HeaderTechnician.Text = L(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "Technician: not set" : ("Technician: " + _config.TechnicianName));
					UpdateDeviceNameHeader();
					_configWriteUtc = lastWriteTimeUtc;
					AddActivity("System", "Reloaded settings changed by the macOS app.");
				}
			}
			catch (Exception ex)
			{
				AddActivity("System", "Shared configuration refresh failed: " + ex.Message);
			}
		}
		if (_startupDataRefreshRequired || !_qaSessionReady || !File.Exists(QaSessionCachePath))
		{
			return;
		}
		try
		{
			DateTime lastWriteTimeUtc2 = File.GetLastWriteTimeUtc(QaSessionCachePath);
			if (!(lastWriteTimeUtc2 <= _qaSessionCacheWriteUtc))
			{
				QaSessionCache? qaSessionCache = ReadQaSessionCache();
				if (qaSessionCache != null)
				{
					RestoreQaSessionCache(qaSessionCache);
					_qaSessionCacheWriteUtc = lastWriteTimeUtc2;
					AddActivity("System", "Reloaded QA notes and final checks changed by the macOS app.");
				}
			}
		}
		catch (Exception ex2)
		{
			AddActivity("System", "Shared QA session refresh failed: " + ex2.Message);
		}
	}

	private void ClearStartupDataForRefresh()
	{
		_serviceTag = "";
		_assetTag = "";
		_warranty = "";
		_warrantyCachedServiceTag = "";
		_batterySummary = "";
		_batteryHealthRating = "";
		_currentBattery = new CurrentBatterySnapshot
		{
			Status = "Refreshing"
		};
		_hardware = new HardwareSnapshot();
		HeaderSerial.Text = L("Service Tag: loading...");
		HeaderAsset.Text = L("Asset: loading...");
		HeaderAssetBubble.Background = Brushes.Transparent;
		HeaderAssetBubble.BorderBrush = Brushes.Transparent;
		HeaderAsset.Foreground = (Brush)FindResource("MutedBrush");
		HeaderWarranty.Text = L("Warranty: loading...");
		HeaderWarranty.ToolTip = "Warranty lookup will run after the Service Tag loads.";
		HeaderBattery.Text = L("Battery Health: loading...");
		HeaderBatteryDots.Text = "\u25CB\u25CB\u25CB\u25CB";
		HeaderBatteryDots.Foreground = (Brush)FindResource("MutedBrush");
		CurrentBatteryPercent.Text = "--%";
		CurrentBatteryFill.Width = 0.0;
		CurrentBatteryPanel.ToolTip = "Current battery status is refreshing.";
		_states["SecureBoot"] = "Working";
		SetBiosButtonState(BiosSecureBootButton, "Working", "Secure Boot");
		SetBiosStatusIcon("Working");
		BiosStatusText.Text = "Reading BIOS settings...";
		if (_hardwareOpen)
		{
			HardwareDetailsBox.Text = "Loading hardware snapshot...";
		}
	}

	private async void ResetQaButton_Click(object sender, RoutedEventArgs e)
	{
		if (MessageBox.Show(this, "Start a new QA and clear the current results, final checks, and notes?", "Start New QA", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		_qaSessionSaveTimer.Stop();
		SaveQaSessionCache();
		_activeQaSessionId = Guid.NewGuid().ToString("N");
		_activeQaSessionStartedAt = DateTime.Now;
		_startupDataRefreshRequired = true;
		_qaSessionReady = false;
		_suppressQaSessionCache = true;
		_processingOperations.Clear();
		_cameraTestRunId++;
		_cameraCleanupTask = null;
		BeginProcessing("New QA");
		_completionCelebrated = false;
		_activity.Clear();
		ActivityBox.Clear();
		ResetQaButton.IsEnabled = false;
		_qaLiveMonitoringActive = true;
		StartExternalDisplayPolling();
		SetStep("WiFi", WifiIcon, WifiMain, WifiDetail, "Waiting", "Wi-Fi not checked yet", "Looking for a connected Wi-Fi IP or visible SSIDs.");
		SetStep("Ethernet", EthernetIcon, EthernetMain, EthernetDetail, "Waiting", "Ethernet not checked yet", "Looking for at least one physical Ethernet adapter that is Up.");
		SetStep("Camera", CameraIcon, CameraMain, CameraDetail, "Waiting", "Camera not checked yet", "Start Camera, then choose Pass or Fail.");
		SetStep("ExternalVideo", ExternalIcon, ExternalMain, ExternalDetail, "Waiting", "External video not checked yet", "Verify video output on the external display.");
		SetStep("Keyboard", KeyboardIcon, KeyboardMain, KeyboardDetail, "Waiting", "Keyboard not checked yet", "Start tester, then choose Pass or Fail.");
		SetStep("Diagnostics", DiagnosticsIcon, DiagnosticsMain, DiagnosticsDetail, "Waiting", "Diagnostics pending", "The diagnostics result will appear when Start New QA finishes processing.");
		FinalHashGroupTagCheck.IsChecked = false;
		FinalCleanedLaptopCheck.IsChecked = false;
		FinalUpdateStockroomsCheck.IsChecked = false;
		FinalTrackpadWorkingCheck.IsChecked = false;
		FinalDeletedUserCheck.IsChecked = false;
		FinalConditionSuitableCheck.IsChecked = false;
		await InitializeUsbPortTestAsync();
		RmaIssueBox.Clear();
		RepairNotesBox.Clear();
		_diagnosticsLogPath = "";
		_diagnosticsRawText = "";
		DiagnosticsRawButton.IsEnabled = false;
		_warrantyWaitingForNetwork = false;
		ClearStartupDataForRefresh();
		try
		{
			if (File.Exists(QaSessionCachePath))
			{
				File.Delete(QaSessionCachePath);
			}
		}
		catch
		{
		}
		AddActivity("Reset", "New QA started and saved QA session cleared. Refreshing device details now.");
		try
		{
			await LoadInitialDataAsync(showStartupSplash: false);
			string currentComputerName = await GetCurrentWindowsComputerNameAsync();
			if (IsUsefulFileIdentifier(currentComputerName))
			{
				_hardware.Computer = currentComputerName;
			}
			_hardware.Computer = PreferredQaComputerName(_hardware.Computer, _serviceTag, _hardware.BiosSerialNumber, _hardware.ChassisSerial, _assetTag);
			_startupDataRefreshRequired = false;
			_suppressQaSessionCache = false;
			SaveQaSessionCache();
			_qaSessionReady = true;
			QaSessionCache? savedSession = ReadQaSessionCache();
			string savedComputerName = savedSession != null ? CachedQaComputerName(savedSession) : ValueOrFallback(_hardware.Computer, _serviceTag);
			AddActivity("System", "Current device name cached for this QA: " + savedComputerName);
			ErrorLog.StartSession(CachedFileIdentifier());
			SaveHardwareSnapshotForNewQa();
			AddActivity("Reset", "New QA device details refreshed.");
		}
		finally
		{
			_startupDataRefreshRequired = false;
			_suppressQaSessionCache = false;
			_qaSessionReady = true;
			ResetQaButton.IsEnabled = true;
			SaveQaSessionCache();
			RefreshCachedSessionPicker();
			EndProcessing("New QA");
		}
	}

	private void SettingsButton_Click(object sender, RoutedEventArgs e)
	{
		SettingsWindow settingsWindow = new SettingsWindow(this, _config, ApplyAppTheme);
		if (settingsWindow.ShowDialog() == true)
		{
			_config = settingsWindow.Config;
			LanguageCatalog.ApplyCulture(_config.AppLanguage);
			ApplyAppTheme(_config.AppTheme);
			WpfLocalization.Apply(this, _config.AppLanguage);
			SaveConfig(_config);
			HeaderTechnician.Text = L(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "Technician: not set" : ("Technician: " + _config.TechnicianName));
			UpdateDeviceNameHeader();
			AddActivity("Settings", settingsWindow.FactoryResetRequested ? "Factory settings restored and saved technician name removed." : $"Settings saved. Theme = {_config.AppTheme}; Technician = {(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "not set" : _config.TechnicianName)}.");
			if (!settingsWindow.FactoryResetRequested && string.IsNullOrWhiteSpace(_config.TechnicianName))
			{
				PromptForTechnicianNameIfNeeded();
				HeaderTechnician.Text = L(string.IsNullOrWhiteSpace(_config.TechnicianName) ? "Technician: not set" : ("Technician: " + _config.TechnicianName));
			}
			_ = RefreshWarrantyAsync();
		}
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource == Shell)
		{
			DragMove();
		}
	}

	#endregion

	#region Logging, process execution, reports, and integration helpers

	private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DragMove();
	}

	private void AddActivity(string section, string message)
	{
		string text = $"[{DateTime.Now:HH:mm:ss}] [{section}] {message}";
		_activity.Add(text);
		TextBox activityBox = ActivityBox;
		activityBox.Text = activityBox.Text + text + Environment.NewLine;
		ActivityBox.ScrollToEnd();
		ErrorLog.WriteActivity(section, message);
		if (ErrorLog.ShouldLogActivity(message))
		{
			ErrorLog.WriteError(section, message);
		}
		ScheduleQaSessionCacheSave();
	}

	private static string ShortActivityText(string text, int maxLength = 180)
	{
		string text2 = Regex.Replace(text.Trim(), "\\s+", " ");
		if (text2.Length > maxLength)
		{
			return text2.Substring(0, maxLength) + "...";
		}
		return text2;
	}

	private async Task<string> RunAudioActionAsync(string action)
	{
		if (!File.Exists(AudioScript))
		{
			return "Audio helper not found.";
		}
		string value = Path.Combine(RuntimeDir, "camera-audio-defaults-before-" + Environment.UserName + ".txt");
		string value2 = Path.Combine(RuntimeDir, "camera-audio-" + Environment.UserName + ".log");
		return await RunProcessCaptureAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{AudioScript}\" -Action {action} -StateFile \"{value}\" -LogFile \"{value2}\"", 45);
	}

	private string CleanupCameraRoll()
	{
		string text = ConfiguredCameraRollPath();
		if (!Directory.Exists(text))
		{
			return "Camera Roll folder was not found: " + text;
		}
		int num = 0;
		int num2 = 0;
		string[] files = Directory.GetFiles(text);
		for (int i = 0; i < files.Length; i++)
		{
			File.Delete(files[i]);
			num++;
		}
		files = Directory.GetDirectories(text);
		for (int i = 0; i < files.Length; i++)
		{
			Directory.Delete(files[i], recursive: true);
			num2++;
		}
		return $"Camera Roll cleanup removed {num} file(s) and {num2} folder(s) from {text}.";
	}

	private int CleanupOldFiles(string folder, int days, string? section = null, string? label = null, bool recursive = false)
	{
		if (!Directory.Exists(folder))
		{
			return 0;
		}
		DateTime dateTime = DateTime.Now.AddDays(-days);
		int num = 0;
		int num2 = 0;
		string[] files = Directory.GetFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
		foreach (string path in files)
		{
			try
			{
				if (!(File.GetLastWriteTime(path) >= dateTime))
				{
					File.Delete(path);
					num++;
				}
			}
			catch (Exception ex)
			{
				num2++;
				if (!string.IsNullOrWhiteSpace(section))
				{
					AddActivity(section, "Could not remove old " + Path.GetFileName(path) + ": " + ex.Message);
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(section) && num > 0)
		{
			AddActivity(section, $"Removed {num} {label ?? "file(s)"} older than {days} days.");
		}
		if (!string.IsNullOrWhiteSpace(section) && num2 > 0)
		{
			AddActivity(section, $"Cleanup skipped {num2} old {label ?? "file(s)"}.");
		}
		if (recursive)
		{
			foreach (string item in from text in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
				orderby text.Length descending
				select text)
			{
				try
				{
					if (!Directory.EnumerateFileSystemEntries(item).Any())
					{
						Directory.Delete(item);
					}
				}
				catch
				{
				}
			}
		}
		return num;
	}

	private async Task<string> PowerShellJsonAsync(string script)
	{
		return ExtractJsonPayload(await PowerShellAsync(script));
	}

	private async Task<string> PowerShellAsync(string script)
	{
		string text = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'\n$utf8NoBom = [System.Text.UTF8Encoding]::new($false)\n[Console]::InputEncoding = $utf8NoBom\n[Console]::OutputEncoding = $utf8NoBom\n$OutputEncoding = $utf8NoBom\n" + script));
		return await RunProcessCaptureAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + text, 45);
	}

	private async Task RunProcessAsync(string file, string args, int timeoutSeconds)
	{
		await RunProcessCaptureAsync(file, args, timeoutSeconds);
	}

	private static Task<string> RunProcessCaptureAsync(string file, IEnumerable<string> arguments, int timeoutSeconds)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(file)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (string argument in arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		return RunProcessCaptureAsync(processStartInfo, timeoutSeconds);
	}

	private static async Task<string> RunProcessCaptureAsync(string file, string args, int timeoutSeconds)
	{
		return await RunProcessCaptureAsync(new ProcessStartInfo(file, args)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		}, timeoutSeconds);
	}

	private static async Task<string> RunProcessCaptureAsync(ProcessStartInfo start, int timeoutSeconds)
	{
		using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + start.FileName);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
		try
		{
			Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
			Task<string> stderrTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync(cts.Token);
			string stdout = await stdoutTask;
			string text = await stderrTask;
			if (process.ExitCode != 0)
			{
				string value = string.Join(Environment.NewLine, new string[2] { stdout, text }.Where((string s) => !string.IsNullOrWhiteSpace(s))).Trim();
				throw new InvalidOperationException(string.IsNullOrWhiteSpace(value) ? $"Exit code {process.ExitCode}" : $"Exit code {process.ExitCode}: {value}");
			}
			return string.Join(Environment.NewLine, new string[2] { stdout, text }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw new TimeoutException(start.FileName + " timed out.");
		}
	}

	private static Dictionary<string, string> JsonToDictionary(string json)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(ExtractJsonPayload(json));
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
		{
			dictionary[item.Name] = ((item.Value.ValueKind == JsonValueKind.String) ? (item.Value.GetString() ?? "") : item.Value.ToString());
		}
		return dictionary;
	}

	private static string ExtractJsonPayload(string output)
	{
		string text = output.Trim();
		int num = text.IndexOf('{');
		int num2 = text.IndexOf('[');
		int num3 = ((num < 0) ? num2 : ((num2 < 0) ? num : Math.Min(num, num2)));
		if (num3 < 0)
		{
			return text;
		}
		int num4 = 0;
		bool flag = false;
		bool flag2 = false;
		for (int i = num3; i < text.Length; i++)
		{
			char c = text[i];
			if (flag)
			{
				if (flag2)
				{
					flag2 = false;
					continue;
				}
				switch (c)
				{
				case '\\':
					flag2 = true;
					break;
				case '"':
					flag = false;
					break;
				}
				continue;
			}
			switch (c)
			{
			case '"':
				flag = true;
				break;
			case '[':
			case '{':
				num4++;
				break;
			case ']':
			case '}':
				num4--;
				if (num4 == 0)
				{
					int num5 = num3;
					return text.Substring(num5, i + 1 - num5);
				}
				break;
			}
		}
		return text.Substring(num3);
	}

	private string HardwareDetailText()
	{
		List<string> lines = new List<string>();
		lines.Add("Device Name: " + ValueOrFallback(_hardware.Computer, Environment.MachineName));
		AddTopLine("Service Tag", _serviceTag);
		AddTopLine("Asset Tag", _assetTag);
		AddTopLine("Warranty", _warranty);
		AddRawTopLine(_batterySummary);
		lines.Add("");
		AddSection("System", new(string, string)[14]
		{
			("Manufacturer", _hardware.Manufacturer),
			("Model", _hardware.Model),
			("System type", _hardware.SystemType),
			("Domain or workgroup", _hardware.Domain),
			("Reported physical memory", _hardware.PhysicalMemory),
			("Hypervisor present", _hardware.HypervisorPresent),
			("Product name", _hardware.ProductName),
			("Product version", _hardware.ProductVersion),
			("UUID", _hardware.Uuid),
			("Baseboard", _hardware.Baseboard),
			("Baseboard serial", _hardware.BaseboardSerial),
			("Chassis manufacturer", _hardware.ChassisManufacturer),
			("Chassis serial", _hardware.ChassisSerial),
			("Chassis asset tag", _hardware.ChassisAssetTag)
		});
		AddSection("BIOS", new(string, string)[5]
		{
			("Manufacturer", _hardware.BiosManufacturer),
			("SMBIOS version", _hardware.SmbiosVersion),
			("BIOS version", _hardware.BiosVersion),
			("Release date", _hardware.BiosReleaseDate),
			("Serial number", _hardware.BiosSerialNumber)
		});
		AddSection("Operating System", new(string, string)[6]
		{
			("Name", _hardware.OsName),
			("Version", _hardware.OsVersion),
			("Build", _hardware.OsBuild),
			("Architecture", _hardware.OsArchitecture),
			("Install date", _hardware.OsInstallDate),
			("Last boot", _hardware.OsLastBoot)
		});
		AddSection("Security", new(string, string)[6]
		{
			("Secure Boot enabled", ValueOrFallback(_hardware.SecureBootEnabled, StatePhrase(_states["SecureBoot"], "True", "False"))),
			("TPM present", _hardware.TpmPresent),
			("TPM ready", _hardware.TpmReady),
			("TPM enabled", _hardware.TpmEnabled),
			("TPM activated", _hardware.TpmActivated),
			("TPM manufacturer version", _hardware.TpmManufacturerVersion)
		});
		AddSection("Hardware", new(string, string)[4]
		{
			("CPU", _hardware.Cpu),
			("Memory", ValueOrFallback(_hardware.Memory, _hardware.PhysicalMemory)),
			("GPU", _hardware.Gpu),
			("Storage", _hardware.Storage)
		});
		return string.Join(Environment.NewLine, lines);
		void AddRawTopLine(string value)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				lines.Add(value.Trim());
			}
		}
		void AddSection(string title, IEnumerable<(string Label, string Value)> values)
		{
			List<string> list = (from v in values
				where !string.IsNullOrWhiteSpace(v.Value)
				select "  " + v.Label + ": " + v.Value.Trim()).ToList();
			if (list.Count != 0)
			{
				lines.Add(title);
				lines.AddRange(list);
				lines.Add("");
			}
		}
		void AddTopLine(string label, string value)
		{
			if (!string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
			{
				lines.Add(label + ": " + value.Trim());
			}
		}
	}

	private static string ValueOrFallback(string value, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		return fallback;
	}

	private string BuildServiceNowRequestDescription()
	{
		string value = ServiceNowModelNumber(_hardware.Model);
		string value2 = (string.IsNullOrWhiteSpace(_serviceTag) ? "serial unavailable" : _serviceTag.Trim());
		string value3 = (string.IsNullOrWhiteSpace(_assetTag) ? "asset unavailable" : _assetTag.Trim());
		return $"Laptop QA | {value} | {value2} | {value3}";
	}

	private static string ServiceNowModelNumber(string? model)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			return "model unavailable";
		}
		string[] array = (from match in Regex.Matches(model.Trim(), "[A-Za-z]*\\d+[A-Za-z0-9-]*")
			select match.Value).ToArray();
		if (array.Length != 0)
		{
			return array[^1];
		}
		return model.Trim();
	}

	private string GetServiceNowRequestUrl()
	{
		if (!string.IsNullOrWhiteSpace(_config.ServiceNowRequestUrl))
		{
			return _config.ServiceNowRequestUrl.Trim();
		}
		return "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982";
	}

	private string GetServiceNowTypeOfRequest()
	{
		if (!string.IsNullOrWhiteSpace(_config.ServiceNowTypeOfRequest))
		{
			return _config.ServiceNowTypeOfRequest.Trim();
		}
		return "Other";
	}

	private string GetServiceNowAssignmentGroupSysId()
	{
		if (!string.IsNullOrWhiteSpace(_config.ServiceNowAssignmentGroupSysId))
		{
			return _config.ServiceNowAssignmentGroupSysId.Trim();
		}
		return "9d144e37bdef1000e25cbf141e60d715";
	}

	private string GetServiceNowAssignmentGroupName()
	{
		if (!string.IsNullOrWhiteSpace(_config.ServiceNowAssignmentGroupName))
		{
			return _config.ServiceNowAssignmentGroupName.Trim();
		}
		return "Desktop Support (Miamisburg) - L2";
	}

	private int GetServiceNowAutomationDelayMilliseconds()
	{
		return Math.Clamp((_config.ServiceNowAutomationDelayMilliseconds <= 0) ? 500 : _config.ServiceNowAutomationDelayMilliseconds, 500, 30000);
	}

	private static string BuildServiceNowAutofillScript(string description, string typeOfRequest, string assignmentGroup, string assignmentGroupDisplay)
	{
		return $"(() => {{\n  const description = {JsonSerializer.Serialize(description)};\n  const assignmentGroup = {JsonSerializer.Serialize(assignmentGroup)};\n  const assignmentGroupDisplay = {JsonSerializer.Serialize(assignmentGroupDisplay)};\n  const typeOfRequest = {JsonSerializer.Serialize(typeOfRequest)};\n\n  function setNativeValue(element, value) {{\n    if (!element) return false;\n    const prototype = Object.getPrototypeOf(element);\n    const descriptor = Object.getOwnPropertyDescriptor(prototype, \"value\");\n    if (descriptor && descriptor.set) descriptor.set.call(element, value);\n    else element.value = value;\n    element.dispatchEvent(new Event(\"input\", {{ bubbles: true }}));\n    element.dispatchEvent(new Event(\"change\", {{ bubbles: true }}));\n    element.dispatchEvent(new Event(\"blur\", {{ bubbles: true }}));\n    return true;\n  }}\n\n  function getGlideForm() {{\n    if (window.g_form) return window.g_form;\n    if (!window.angular) return null;\n    for (const element of document.querySelectorAll(\"[glide-form], [field]\")) {{\n      try {{\n        const scope = angular.element(element).scope();\n        if (scope && typeof scope.getGlideForm === \"function\") return scope.getGlideForm();\n        if (scope && scope.g_form) return scope.g_form;\n      }} catch {{}}\n    }}\n    return null;\n  }}\n\n  function updateAngularField(element, value, displayValue) {{\n    if (!window.angular || !element) return false;\n    try {{\n      const angularElement = angular.element(element);\n      const scopes = [angularElement.scope(), angularElement.isolateScope()].filter(Boolean);\n      for (const scope of scopes) {{\n      if (scope && scope.field) {{\n        scope.field.value = value;\n        scope.field.stagedValue = value;\n        if (displayValue) {{\n          scope.field.displayValue = displayValue;\n          scope.field.display_value = displayValue;\n          scope.field.displayValueStaged = displayValue;\n        }}\n        if (scope.field.reference) {{\n          scope.field.reference.value = value;\n          if (displayValue) scope.field.reference.display_value = displayValue;\n        }}\n        if (typeof scope.stagedValueChange === \"function\") scope.stagedValueChange();\n        if (typeof scope.fieldValueChanged === \"function\") scope.fieldValueChanged();\n        scope.$applyAsync();\n        return true;\n      }}\n      }}\n    }} catch {{}}\n    return false;\n  }}\n\n  function updateSelect2Display(fieldId, displayText) {{\n    const chosen = document.querySelector(`#s2id_${{fieldId}} .select2-chosen`);\n    if (chosen) chosen.textContent = displayText;\n    const container = document.getElementById(`s2id_${{fieldId}}`);\n    if (container) {{\n      container.classList.remove(\"select2-default\");\n      container.classList.add(\"select2-allowclear\");\n    }}\n  }}\n\n  function updateSelect2Value(fieldId, value, displayText) {{\n    let ok = false;\n    const element = document.getElementById(fieldId);\n    const jq = window.jQuery || window.$;\n    if (jq && element) {{\n      try {{\n        jq(element).val(value);\n        ok = true;\n      }} catch {{}}\n      try {{\n        jq(element).select2(\"data\", {{ id: value, text: displayText || value }});\n        ok = true;\n      }} catch {{}}\n      try {{\n        jq(element).trigger(\"change\");\n        ok = true;\n      }} catch {{}}\n    }}\n    if (displayText) updateSelect2Display(fieldId, displayText);\n    return ok;\n  }}\n\n  function setRelatedInputs(fieldName, fieldId, value) {{\n    let ok = false;\n    const selectors = [\n      `#${{fieldId}}`,\n      `[name=\"${{fieldName}}\"]`,\n      `[name=\"${{fieldId}}\"]`,\n      `input[id$=\"${{fieldName}}\"]`\n    ];\n    for (const selector of selectors) {{\n      for (const input of document.querySelectorAll(selector)) {{\n        ok = setNativeValue(input, value) || ok;\n        input.setAttribute(\"value\", value);\n      }}\n    }}\n    return ok;\n  }}\n\n  function setSelectByTextOrValue(id, text) {{\n    const select = document.getElementById(id);\n    if (!select) return false;\n    const option = [...select.options].find(o => o.text.trim().toLowerCase() === text.toLowerCase())\n      || [...select.options].find(o => o.value.trim().toLowerCase() === text.toLowerCase());\n    const value = option ? option.value : text;\n    const ok = setNativeValue(select, value);\n    updateAngularField(select, value, text);\n    updateSelect2Display(id, text);\n    return ok;\n  }}\n\n  function setCatalogValue(fieldName, fieldId, value, displayValue) {{\n    let ok = false;\n    const form = getGlideForm();\n    if (form && typeof form.setValue === \"function\") {{\n      try {{\n        form.setValue(fieldName, value, displayValue || value);\n        ok = true;\n      }} catch {{\n        try {{\n          form.setValue(fieldName, value);\n          ok = true;\n        }} catch {{}}\n      }}\n    }}\n    const element = document.getElementById(fieldId);\n    ok = setNativeValue(element, value) || ok;\n    ok = setRelatedInputs(fieldName, fieldId, value) || ok;\n    ok = updateAngularField(element, value, displayValue) || ok;\n    ok = updateSelect2Value(fieldId, value, displayValue) || ok;\n    if (displayValue) updateSelect2Display(fieldId, displayValue);\n    return ok;\n  }}\n\n  const results = [];\n  results.push([\"Type of request\", setCatalogValue(\"type_of_request\", \"sp_formfield_type_of_request\", typeOfRequest, typeOfRequest) || setSelectByTextOrValue(\"sp_formfield_type_of_request\", typeOfRequest)]);\n  results.push([\"Assignment group\", setCatalogValue(\"assignment_group\", \"sp_formfield_assignment_group\", assignmentGroup, assignmentGroupDisplay)]);\n  results.push([\"Description\", setNativeValue(document.getElementById(\"sp_formfield_describe_request\"), description)]);\n  console.log(\"Laptop QA ServiceNow autofill complete:\", results.map(([name, ok]) => `${{ok ? \"OK\" : \"Missing\"}} - ${{name}}`).join(\"; \"));\n}})();";
	}

	private static string BuildServiceNowBookmarklet(string description, string typeOfRequest, string assignmentGroup, string assignmentGroupDisplay)
	{
		string text = Regex.Replace(BuildServiceNowAutofillScript(description, typeOfRequest, assignmentGroup, assignmentGroupDisplay), "\\s+", " ").Trim();
		return "javascript:" + text;
	}

	private static string BuildServiceNowAutomationPowerShell(string serviceNowRequestUrl, int startDelayMilliseconds, string requestDescription)
	{
		return $"$ErrorActionPreference = 'SilentlyContinue'\n$url = {PowerShellLiteral(serviceNowRequestUrl)}\n$requestDescription = {PowerShellLiteral(requestDescription)}\nStart-Process 'msedge.exe' $url\nStart-Sleep -Milliseconds {startDelayMilliseconds}\n$shell = New-Object -ComObject WScript.Shell\n\nAdd-Type @\"\nusing System;\nusing System.Runtime.InteropServices;\npublic static class WinFocus {{\n  [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);\n  [DllImport(\"user32.dll\")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);\n  public const int KEYEVENTF_KEYUP = 0x0002;\n  public static void PasteClipboard() {{\n    keybd_event(0x11, 0, 0, UIntPtr.Zero);\n    keybd_event(0x56, 0, 0, UIntPtr.Zero);\n    keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);\n    keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);\n  }}\n}}\n\"@\n\n$target = $null\nfor ($attempt = 0; $attempt -lt 18; $attempt++) {{\n  $target = Get-Process msedge | Where-Object {{\n    $_.MainWindowHandle -ne 0 -and (\n      $_.MainWindowTitle -match 'Generic Service Request' -or\n      $_.MainWindowTitle -match 'LN IT Service Portal' -or\n      $_.MainWindowTitle -match 'ServiceNow'\n    )\n  }} | Select-Object -First 1\n  if ($target) {{ break }}\n  Start-Sleep -Milliseconds 300\n}}\n\nif (-not $target) {{\n  $target = Get-Process msedge | Where-Object {{ $_.MainWindowHandle -ne 0 }} | Sort-Object StartTime -Descending | Select-Object -First 1\n}}\n\nif (-not $target) {{ throw 'Could not find an Edge window to automate.' }}\n[WinFocus]::SetForegroundWindow($target.MainWindowHandle) | Out-Null\nStart-Sleep -Milliseconds 350\n$shell.AppActivate($target.Id) | Out-Null\nStart-Sleep -Milliseconds 350\n$shell.SendKeys('^l')\nStart-Sleep -Milliseconds 250\n$shell.SendKeys('javascript:')\nStart-Sleep -Milliseconds 150\n[WinFocus]::PasteClipboard()\nStart-Sleep -Milliseconds 250\n$shell.SendKeys('{{ENTER}}')\nStart-Sleep -Milliseconds 750\nSet-Clipboard -Value $requestDescription";
	}

	private static string PowerShellLiteral(string value)
	{
		return "'" + (value ?? "").Replace("'", "''") + "'";
	}

	private static void RunServiceNowAutomation(string serviceNowRequestUrl, int startDelayMilliseconds, string requestDescription)
	{
		string item = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildServiceNowAutomationPowerShell(serviceNowRequestUrl, startDelayMilliseconds, requestDescription)));
		Process.Start(new ProcessStartInfo("powershell.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", item }
		});
	}

	private string BuildQaSheetHtml()
	{
		string text = ((FinalHashGroupTagCheck.IsChecked == true) ? "Ok" : "Waiting");
		string text2 = ((FinalCleanedLaptopCheck.IsChecked == true) ? "Ok" : "Waiting");
		string text3 = ((FinalDeletedUserCheck.IsChecked == true) ? "Ok" : "Waiting");
		string text4 = ((FinalUpdateStockroomsCheck.IsChecked == true) ? "Ok" : "Waiting");
		string text5 = ((FinalTrackpadWorkingCheck.IsChecked == true) ? "Ok" : "Waiting");
		string text6 = ((FinalConditionSuitableCheck.IsChecked == true) ? "Ok" : "Waiting");
		string item = ((!_usbPortTestFinished) ? "Waiting" : (_usbPorts.Any((UsbPortCache port) => port.Failed) ? "Bad" : "Ok"));
		string item2 = ((_usbPorts.Count == 0) ? "USB port count unavailable from BIOS connector data." : $"{_usbPorts.Count((UsbPortCache port) => port.Passed)} passed, {_usbPorts.Count((UsbPortCache port) => port.Failed)} failed, {_usbPorts.Count((UsbPortCache port) => !port.Passed && !port.Failed)} pending.");
		(string, string, string, string)[] source = new(string, string, string, string)[14]
		{
			("2", "Wi-Fi connected or SSIDs visible", _states["WiFi"], Detail("WiFi", "Wi-Fi not checked yet. Looking for a connected Wi-Fi IP or visible SSIDs.")),
			("2", "Ethernet adapter is Up", _states["Ethernet"], Detail("Ethernet", "Ethernet not checked yet. Looking for at least one physical Ethernet adapter that is Up.")),
			("3", "Camera, audio restore, and Camera Roll cleanup", _states["Camera"], Detail("Camera", "Camera not checked yet. Start Camera, then choose Pass or Fail.")),
			("4", "External display video verified", _states["ExternalVideo"], Detail("ExternalVideo", "External video not checked yet. Verify video output on the external display.")),
			("5", "Keyboard test result", _states["Keyboard"], Detail("Keyboard", "Keyboard not checked yet. Start tester, then choose Pass or Fail.")),
			("6", "Dell preboot diagnostics", _states["Diagnostics"], Detail("Diagnostics", "Diagnostics log not found.")),
			("7", "USB ports verified", item, item2),
			("", "Battery health checked", "Ok", _batterySummary),
			("8", "Hash and group tag checked", text, (text == "Ok") ? "Hash and group tag checked off." : "Hash and group tag not checked off."),
			("8", "Laptop cleaned", text2, (text2 == "Ok") ? "Cleaned laptop checked off." : "Cleaned laptop not checked off."),
			("8", "Removed User from Laptop in Intune", text3, (text3 == "Ok") ? "User removal from laptop in Intune checked off." : "User removal from laptop in Intune not checked off."),
			("8", "Update Stockrooms", text4, (text4 == "Ok") ? "Stockrooms updated." : "Stockrooms not updated."),
			("8", "Trackpad working", text5, (text5 == "Ok") ? "Trackpad working checked off." : "Trackpad working not checked off."),
			("8", "Physical condition suitable for use", text6, (text6 == "Ok") ? "Physical laptop condition confirmed suitable for use." : "Physical laptop condition not confirmed suitable for use.")
		};
		string text7 = (source.Any(((string, string, string, string) r) => r.Item3 == "Bad") ? "Needs Attention" : (source.Any(((string, string, string, string) r) => r.Item3 == "Warning") ? "Warning" : (source.All(((string, string, string, string) r) => r.Item3 == "Ok" || r.Item3 == "Ignored") ? "Passed" : "Incomplete")));
		string value = ((text7 == "Passed") ? "overall-pass" : ((text7 == "Needs Attention") ? "overall-fail" : "overall-incomplete"));
		string value2 = string.Join(Environment.NewLine, source.Select(((string, string, string, string) r) => $"                        <tr>\n                            <td><div class=\"task\">{H(r.Item2)}</div></td>\n                            <td><span class=\"status {ClassName(r.Item3)}\">{H(Label(r.Item3))}</span></td>\n                            <td>{H(r.Item4)}</td>\n                        </tr>"));
		DateTime now = DateTime.Now;
		string value3 = WarrantyDisplayText();
		string value4 = JsonSerializer.Serialize(BuildServiceNowRequestDescription()).Replace("</", "<\\/");
		string value5 = JsonSerializer.Serialize(GetServiceNowRequestUrl()).Replace("</", "<\\/");
		return $"<!doctype html>\n<html lang=\"en\">\n<head>\n    <meta charset=\"utf-8\">\n    <title>Laptop QA Sheet - {H(Environment.MachineName)}</title>\n    <style>\n        @page {{ size: Letter; margin: 0.45in; }}\n        * {{ box-sizing: border-box; }}\n        html {{ background: transparent; }}\n        body {{ margin: 0; background: transparent; color: #13252d; font-family: \"Segoe UI\", Arial, sans-serif; font-size: 12px; line-height: 1.35; }}\n        .print-action {{ width: 8in; margin: 14px auto 0; display: flex; justify-content: flex-end; align-items: center; gap: 8px; }}\n        .print-action button {{ border: 0; border-radius: 7px; padding: 9px 14px; color: white; background: #244f5c; font-weight: 700; cursor: pointer; }}\n        .print-action button.secondary {{ background: #60757e; }}\n        .sheet {{ width: 8in; min-height: 10.1in; margin: 0; background: #ffffff; border: 0; border-radius: 16px; box-shadow: none; overflow: hidden; }}\n        .titlebar {{ display: grid; grid-template-columns: 1fr 1.55in; gap: 18px; padding: 22px 26px 20px; color: #ffffff; background: linear-gradient(135deg, #18333d 0%, #2d5965 58%, #5f858d 100%); }}\n        h1 {{ margin: 0 0 4px; font-size: 25px; letter-spacing: 0; line-height: 1.05; }}\n        .overall {{ align-self: start; justify-self: end; min-width: 1.35in; padding: 11px 12px; border: 1px solid rgba(255,255,255,0.32); border-radius: 9px; background: rgba(255,255,255,0.12); text-align: center; }}\n        .overall .label {{ display: block; color: #d8e8ec; font-size: 10px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }}\n        .overall .value {{ display: block; margin-top: 4px; color: #ffffff; font-size: 16px; font-weight: 800; }}\n        .content {{ padding: 18px 26px 24px; }}\n        .meta-grid {{ display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 8px; margin-bottom: 14px; }}\n        .field, .note-box {{ border: 1px solid #cbd9df; border-radius: 7px; background: #f7fafb; }}\n        .field {{ min-height: 46px; padding: 8px 9px; }}\n        .label {{ display: block; margin-bottom: 4px; color: #52666f; font-size: 9.5px; font-weight: 800; letter-spacing: 0.08em; text-transform: uppercase; }}\n        .value {{ color: #13252d; font-size: 12px; font-weight: 700; overflow-wrap: anywhere; }}\n        .hardware-grid {{ display: grid; grid-template-columns: 1fr 1fr; column-gap: 14px; row-gap: 0; margin: 0 0 11px; padding: 8px 10px; border: 1px solid #cbd9df; border-radius: 7px; background: #fbfcfd; }}\n        .hardware-row {{ display: grid; grid-template-columns: 0.72in 1fr; gap: 8px; min-height: 24px; align-items: baseline; padding: 3px 0; border-bottom: 1px solid #e2eaed; }}\n        .hardware-row:nth-last-child(-n+2) {{ border-bottom: 0; }}\n        .hardware-row .label {{ margin: 0; font-size: 9px; }}\n        .hardware-row .value {{ font-size: 11px; font-weight: 700; line-height: 1.25; }}\n        .section-title {{ margin: 15px 0 7px; color: #18333d; font-size: 13px; font-weight: 800; letter-spacing: 0.04em; text-transform: uppercase; }}\n        table {{ width: 100%; border-collapse: collapse; table-layout: fixed; }}\n        .qa-table th {{ padding: 8px; color: #ffffff; background: #244f5c; border: 1px solid #244f5c; font-size: 10px; letter-spacing: 0.06em; text-align: left; text-transform: uppercase; }}\n        .qa-table td {{ padding: 9px 8px; border: 1px solid #d7e1e5; vertical-align: middle; }}\n        .qa-table tr:nth-child(even) td {{ background: #f6f9fa; }}\n        .task {{ color: #13252d; font-weight: 800; }}\n        .status {{ display: inline-block; min-width: 0.82in; padding: 4px 7px; border-radius: 999px; font-size: 10px; font-weight: 900; text-align: center; text-transform: uppercase; }}\n        .status.pass {{ color: #0f5132; background: #d9f5e6; border: 1px solid #a9e6c1; }}\n        .status.fail {{ color: #842029; background: #fde2e4; border: 1px solid #f3b4bb; }}\n        .status.warning {{ color: #6b4d00; background: #fff2c2; border: 1px solid #f2d36b; }}\n        .status.progress {{ color: #614a00; background: #fff2c2; border: 1px solid #f2d36b; }}\n        .status.not-run {{ color: #465a62; background: #eef3f5; border: 1px solid #ccd8de; }}\n        .notes {{ display: grid; gap: 8px; margin-top: 8px; }}\n        .note-box {{ padding: 8px 9px; background: #fbfcfd; }}\n        .note-value {{ min-height: 52px; padding: 4px 0 2px; color: #13252d; font-size: 12px; font-weight: 650; white-space: pre-wrap; overflow-wrap: anywhere; outline: none; }}\n        .note-value.tall {{ min-height: 78px; }}\n        .footer {{ display: flex; justify-content: space-between; margin-top: 12px; padding-top: 8px; border-top: 1px solid #d7e1e5; color: #60757e; font-size: 9.5px; }}\n        @media print {{ body {{ background: #ffffff; }} .print-action {{ display: none; }} .sheet {{ width: auto; min-height: 0; margin: 0; border: 0; border-radius: 0; box-shadow: none; overflow: visible; }} .content {{ padding-bottom: 0; }} .qa-table tr, .field, .note-box {{ break-inside: avoid; }} }}\n    </style>\n</head>\n<body>\n    <main class=\"sheet\">\n        <header class=\"titlebar\">\n            <div><h1>Laptop QA Testing</h1></div>\n            <div class=\"overall\"><span class=\"label\">Overall</span><span class=\"value {value}\">{H(text7)}</span></div>\n        </header>\n        <section class=\"content\">\n            <div class=\"meta-grid\">\n                <div class=\"field\"><span class=\"label\">Computer</span><span class=\"value\">{H(Environment.MachineName)}</span></div>\n                <div class=\"field\"><span class=\"label\">Technician</span><span class=\"value\">{H(_config.TechnicianName)}</span></div>\n                <div class=\"field\"><span class=\"label\">Date</span><span class=\"value\">{H(now.ToString("yyyy-MM-dd HH:mm"))}</span></div>\n                <div class=\"field\"><span class=\"label\">Manufacturer</span><span class=\"value\">{H(_hardware.Manufacturer)}</span></div>\n                <div class=\"field\"><span class=\"label\">Model</span><span class=\"value\">{H(_hardware.Model)}</span></div>\n                <div class=\"field\"><span class=\"label\">Service Tag</span><span class=\"value\">{H(_serviceTag)}</span></div>\n                <div class=\"field\"><span class=\"label\">Asset Number</span><span class=\"value\">{H(_assetTag)}</span></div>\n                <div class=\"field\"><span class=\"label\">Warranty</span><span class=\"value\">{H(value3)}</span></div>\n            </div>\n            <div class=\"section-title\">Hardware Specs</div>\n            <div class=\"hardware-grid\">\n                <div class=\"hardware-row\"><span class=\"label\">CPU</span><span class=\"value\">{H(_hardware.Cpu)}</span></div>\n                <div class=\"hardware-row\"><span class=\"label\">Memory</span><span class=\"value\">{H(ValueOrFallback(_hardware.Memory, _hardware.PhysicalMemory))}</span></div>\n                <div class=\"hardware-row\"><span class=\"label\">GPU</span><span class=\"value\">{H(_hardware.Gpu)}</span></div>\n                <div class=\"hardware-row\"><span class=\"label\">Storage</span><span class=\"value\">{H(_hardware.Storage)}</span></div>\n            </div>\n            <div class=\"section-title\">QA Results</div>\n            <table class=\"qa-table\">\n                <colgroup><col style=\"width: 3.24in;\"><col style=\"width: 1.02in;\"><col></colgroup>\n                <thead><tr><th>Task</th><th>Status</th><th>Detail</th></tr></thead>\n                <tbody>\n{value2}\n                </tbody>\n            </table>\n            <div class=\"section-title\">Notes</div>\n            <div class=\"notes\">\n                <div class=\"note-box\"><span class=\"label\">RMA Issues</span><div class=\"note-value\" contenteditable=\"true\">{H(RmaIssueBox.Text.Trim())}</div></div>\n                <div class=\"note-box\"><span class=\"label\">Repair Notes</span><div class=\"note-value tall\" contenteditable=\"true\">{H(RepairNotesBox.Text.Trim())}</div></div>\n            </div>\n            <div class=\"footer\">\n                <span>Generated: {H(now.ToString("yyyy-MM-dd HH:mm:ss"))}</span>\n            </div>\n        </section>\n    </main>\n    <script>\n        const serviceNowRequestUrl = {value5};\n        const serviceNowRequestDescription = {value4};\n\n        function copyTextToClipboard(text) {{\n            const textarea = document.createElement('textarea');\n            textarea.value = text;\n            textarea.setAttribute('readonly', '');\n            textarea.style.position = 'fixed';\n            textarea.style.left = '-9999px';\n            textarea.style.top = '0';\n            document.body.appendChild(textarea);\n            textarea.focus();\n            textarea.select();\n\n            let copied = false;\n            try {{\n                copied = document.execCommand('copy');\n            }} finally {{\n                document.body.removeChild(textarea);\n            }}\n\n            return copied;\n        }}\n\n        function openServiceNowRequest() {{\n            copyTextToClipboard(serviceNowRequestDescription);\n            window.open(serviceNowRequestUrl, \"_blank\", \"noopener\");\n        }}\n    </script>\n</body>\n</html>";
		static string ClassName(string state)
		{
			return state switch
			{
				"Ok" => "pass",
				"Bad" => "fail",
				"Warning" => "warning",
				"Working" => "progress",
				_ => "not-run",
			};
		}
		string Detail(string key, string fallback)
		{
			string valueOrDefault = _details.GetValueOrDefault(key, "");
			string text8 = ((!(_states.GetValueOrDefault(key, "") == "Waiting")) ? valueOrDefault : fallback);
			string text9 = text8;
			if (!string.IsNullOrWhiteSpace(text9))
			{
				return text9;
			}
			return fallback;
		}
		static string H(string? text8)
		{
			return WebUtility.HtmlEncode(text8 ?? "");
		}
		static string Label(string state)
		{
			return state switch
			{
				"Ok" => "Pass",
				"Bad" => "Fail",
				"Warning" => "Warning",
				"Working" => "In Progress",
				_ => "Not Run",
			};
		}
	}

	private static string PsQuote(string value)
	{
		return value.Replace("'", "''");
	}

	private static string SafeFile(string value, string fallback)
	{
		string cleaned = Regex.Replace(string.IsNullOrWhiteSpace(value) ? fallback : value, "[<>:\"/\\\\|?*\\x00-\\x1f]+", "-").Trim(' ', '.', '-');
		return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
	}

	private static string RedactServiceTag(string text, string serviceTag)
	{
		if (!string.IsNullOrWhiteSpace(serviceTag))
		{
			return text.Replace(serviceTag.Trim(), "[Service Tag]", StringComparison.OrdinalIgnoreCase);
		}
		return text;
	}

	#endregion
}
