using System;
using System.Diagnostics;
using System.Windows;

namespace LaptopQA.Windows;

internal static class ServiceNowRequestLauncher
{
	public static void OpenWithClipboardFallback(string requestUrl, string requestDescription)
	{
		if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
		{
			throw new InvalidOperationException("The configured ServiceNow request URL is invalid.");
		}

		Clipboard.SetText(requestDescription);
		Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
		{
			UseShellExecute = true
		});
	}
}
