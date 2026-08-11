namespace LaptopQA.Windows;

public sealed class UsbPortCache
{
	public string Label { get; set; } = "";

	public bool Passed { get; set; }

	public bool Failed { get; set; }

	public string LocationPath { get; set; } = "";

	public string DeviceName { get; set; } = "";
}
