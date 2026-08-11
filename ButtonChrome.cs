using System.Windows.Controls;
using System.Windows.Markup;

namespace LaptopQA.Windows;

internal static class ButtonChrome
{
	public static ControlTemplate RoundedTemplate()
	{
		return (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"{x:Type Button}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n    <Border x:Name=\"Bd\"\n            Background=\"{TemplateBinding Background}\"\n            BorderBrush=\"{TemplateBinding BorderBrush}\"\n            BorderThickness=\"{TemplateBinding BorderThickness}\"\n            CornerRadius=\"14\"\n            Padding=\"{TemplateBinding Padding}\">\n        <ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>\n    </Border>\n    <ControlTemplate.Triggers>\n        <Trigger Property=\"IsMouseOver\" Value=\"True\">\n            <Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.88\"/>\n        </Trigger>\n        <Trigger Property=\"IsPressed\" Value=\"True\">\n            <Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.72\"/>\n        </Trigger>\n        <Trigger Property=\"IsEnabled\" Value=\"False\">\n            <Setter TargetName=\"Bd\" Property=\"Opacity\" Value=\"0.36\"/>\n        </Trigger>\n    </ControlTemplate.Triggers>\n</ControlTemplate>");
	}
}
