namespace LaptopQA.Windows;

public sealed class CurrentBatterySnapshot
{
	public bool IsPresent { get; set; }

	public int Percent { get; set; } = -1;

	public string Status { get; set; } = "Unavailable";

	public bool IsCharging { get; set; }

	public bool IsPluggedIn { get; set; }
}
