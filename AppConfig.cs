namespace LaptopQATestingV4;

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
}
