namespace LaptopQA.Windows;

public sealed record ExternalDisplayInfo(string DisplayName, string MonitorName, int Width, int Height, int X, int Y, bool IsPrimary)
{
	public string Summary
	{
		get
		{
			string value = (string.IsNullOrWhiteSpace(MonitorName) ? DisplayName : MonitorName);
			string value2 = ((Width > 0 && Height > 0) ? $" {Width}x{Height}" : "");
			string value3 = (IsPrimary ? "primary" : "secondary");
			return $"{value}{value2} ({value3})";
		}
	}
}
