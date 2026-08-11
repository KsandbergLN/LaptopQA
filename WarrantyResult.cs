namespace LaptopQA.Windows;

public sealed record WarrantyResult(bool Found, string ExpirationDateText, string Message)
{
	public static WarrantyResult NotFound(string message)
	{
		return new WarrantyResult(Found: false, "", message);
	}
}
