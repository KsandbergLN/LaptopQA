namespace LaptopQA.Windows;

public sealed class AppConfig
{
	public string TechnicianName { get; set; } = "";

	public string AppTheme { get; set; } = "Light";

	public string AppLanguage { get; set; } = "en-US";

	public string CameraRoll { get; set; } = "C:\\Users\\defaultuser0\\Pictures\\Camera Roll";

	public string DellDiagnosticsLogFolder { get; set; } = "";

	public int CameraRollCleanupTimeoutSeconds { get; set; } = 30;

	public int CameraRollCleanupRetryDelaySeconds { get; set; } = 2;

	public int WifiRescanEthernetDisableDelaySeconds { get; set; } = 3;

	public int EthernetRestoreDelaySeconds { get; set; } = 2;

	public string DellWarrantyCliPath { get; set; } = "";

	public string AutopilotGroupTag { get; set; } = "LNG AAD";

	public string QaComputerNameFormat { get; set; } = "LNG-{serial}";

	public string ServiceNowRequestUrl { get; set; } = "https://reedelsevier.service-now.com/reed?id=sc_cat_item&sys_id=23302f892bed96006f7581afe8da1547&sysparm_category=c69e7347db824740d2cbf2f9af961982";

	public string ServiceNowTypeOfRequest { get; set; } = "Other";

	public string ServiceNowAssignmentGroupName { get; set; } = "Desktop Support (Miamisburg) - L2";

	public string ServiceNowAssignmentGroupSysId { get; set; } = "9d144e37bdef1000e25cbf141e60d715";

	public int ServiceNowAutomationDelayMilliseconds { get; set; } = 500;

	public string CheckHashAndGroupTagUrl { get; set; } = "https://intune.microsoft.com/#view/Microsoft_Intune_Enrollment/AutopilotDevices.ReactView/filterOnManualRemediationRequired~/false";

	public string RemoveUserFromIntuneUrl { get; set; } = "https://intune.microsoft.com/#view/Microsoft_Intune_DeviceSettings/DevicesWindowsMenu/~/windowsDevices";

	public string UpdateStockroomsUrl { get; set; } = "https://reedelsevier.service-now.com/now/nav/ui/classic/params/target/alm_hardware_list.do%3Fsysparm_first_row%3D1%26sysparm_query%3Dserial_number%3D{SERIAL}%26sysparm_query_encoded%3Dserial_number%3D{SERIAL}%26sysparm_view%3D";
}
