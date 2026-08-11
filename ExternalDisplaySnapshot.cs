using System.Collections.Generic;
using System.Linq;

namespace LaptopQA.Windows;

public sealed record ExternalDisplaySnapshot(int ActiveDisplayCount, IReadOnlyList<ExternalDisplayInfo> Displays)
{
	public bool PhysicalExternalConnected { get; init; }

	public string ConnectionDetail { get; init; } = "";

	public bool HasExternalDisplay
	{
		get
		{
			if (!PhysicalExternalConnected)
			{
				return ActiveDisplayCount > 1;
			}
			return true;
		}
	}

	public string DetailText
	{
		get
		{
			if (PhysicalExternalConnected && ActiveDisplayCount <= 1 && !string.IsNullOrWhiteSpace(ConnectionDetail))
			{
				return ConnectionDetail;
			}
			if (Displays.Count != 0)
			{
				string value = string.Join("; ", Displays.Select((ExternalDisplayInfo display) => display.Summary));
				return $"Active displays: {value}. {ActiveDisplayCount} active displays detected by Windows.";
			}
			return $"{ActiveDisplayCount} active displays detected by Windows.";
		}
	}
}
