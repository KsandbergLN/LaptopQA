using System.Runtime.InteropServices;

namespace LaptopQATestingV4;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DisplayDevice
{
	public int cb;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
	public string DeviceName;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string DeviceString;

	public int StateFlags;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string DeviceID;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string DeviceKey;
}
