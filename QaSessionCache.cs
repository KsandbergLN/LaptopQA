using System;
using System.Collections.Generic;

namespace LaptopQA.Windows;

public sealed class QaSessionCache
{
	public string SessionId { get; set; } = "";

	public DateTime StartedAt { get; set; }

	public DateTime SavedAt { get; set; }

	public bool StartupDataSaved { get; set; }

	public string ServiceTag { get; set; } = "";

	public string AssetTag { get; set; } = "";

	public string Warranty { get; set; } = "";

	public string WarrantyCachedServiceTag { get; set; } = "";

	public string BatterySummary { get; set; } = "";

	public string BatteryHealthRating { get; set; } = "";

	public CurrentBatterySnapshot? CurrentBattery { get; set; }

	public HardwareSnapshot? Hardware { get; set; }

	public string SecureBootState { get; set; } = "";

	public string BiosStatusText { get; set; } = "";

	public Dictionary<string, QaStepCache> Steps { get; set; } = new Dictionary<string, QaStepCache>();

	public bool? FinalHashGroupTag { get; set; }

	public bool? FinalCleanedLaptop { get; set; }

	public bool? FinalUpdateStockrooms { get; set; }

	public bool? FinalTrackpadWorking { get; set; }

	public bool? FinalDeletedUser { get; set; }

	public bool? FinalConditionSuitableForUse { get; set; }

	public bool UsbPortTestFinished { get; set; }

	public List<UsbPortCache> UsbPorts { get; set; } = new List<UsbPortCache>();

	public string RmaIssues { get; set; } = "";

	public string RepairNotes { get; set; } = "";

	public string DiagnosticsLogPath { get; set; } = "";

	public string DiagnosticsRawText { get; set; } = "";

	public string HashGroupTagStatusText { get; set; } = "";

	public string HashGroupTagState { get; set; } = "";

	public string HashGroupTagDetail { get; set; } = "";
}
