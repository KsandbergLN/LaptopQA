param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SetSpeaker', 'Restore', 'List', 'Disable', 'Enable')]
    [string] $Action,

    [Parameter(Mandatory = $true)]
    [string] $StateFile,

    [string] $LogFile = ''
)

$ErrorActionPreference = 'Stop'

if ($Action -eq 'Disable') {
    $Action = 'SetSpeaker'
} elseif ($Action -eq 'Enable') {
    $Action = 'Restore'
}

if ($LogFile) {
    $logDir = Split-Path -Parent $LogFile
    if ($logDir) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }

    try {
        Start-Transcript -Path $LogFile -Append | Out-Null
    } catch {
        Write-Warning ("Could not start log file {0}: {1}" -f $LogFile, $_.Exception.Message)
    }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, IntPtr ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
public class PolicyConfigClient
{
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, out IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, out long pmftDefaultPeriod, out long pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ref long pmftPeriod);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr pMode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr key, out IntPtr pv);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr key, IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bVisible);
}

public static class AudioDefaults
{
    public static string GetDefaultEndpointId(EDataFlow flow, ERole role)
    {
        IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice device;
        Check(enumerator.GetDefaultAudioEndpoint(flow, role, out device));

        string id;
        Check(device.GetId(out id));
        return id;
    }

    public static void SetDefaultEndpoint(string endpointId, ERole role)
    {
        IPolicyConfig policy = (IPolicyConfig)new PolicyConfigClient();
        Check(policy.SetDefaultEndpoint(endpointId, role));
    }

    private static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
'@

function Get-AudioEndpoints {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Render', 'Capture')]
        [string] $Flow
    )

    $registryPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\$Flow"
    $endpointPrefix = if ($Flow -eq 'Render') { '0.0.0' } else { '0.0.1' }

    return @(Get-ChildItem -Path $registryPath -ErrorAction Stop | ForEach-Object {
        $device = Get-ItemProperty -LiteralPath $_.PSPath
        $properties = Get-ItemProperty -LiteralPath (Join-Path $_.PSPath 'Properties')
        $name = $properties.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
        $driverName = $properties.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6'
        $endpointId = "{$endpointPrefix.00000000}.$($_.PSChildName)"
        $label = $name

        if ($driverName) {
            $label = "{0} ({1})" -f $name, $driverName
        }

        [pscustomobject]@{
            Id = $endpointId
            Name = $label
            Flow = $Flow
            DeviceState = [int]$device.DeviceState
            Active = [int]$device.DeviceState -eq 1
            Score = if ($Flow -eq 'Render') { Get-SpeakerScore -Name $label } else { Get-MicrophoneScore -Name $label }
        }
    })
}

function Get-PlaybackEndpoints {
    return @(Get-AudioEndpoints -Flow 'Render')
}

function Get-RecordingEndpoints {
    return @(Get-AudioEndpoints -Flow 'Capture')
}

function Get-SpeakerScore {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($Name -notmatch '(?i)\bspeakers?\b') {
        return -1
    }

    $externalRegex = '(?i)(dell|wd19|dock|usb|bluetooth|headset|earphone|receiver|display|monitor|hdmi|lg|nvidia|audio receiver|display audio|line\b|line out|line-out|lineout)'

    if ($Name -match $externalRegex) {
        return -1
    }

    if ($Name -match '(?i)realtek' -and $Name -match '(?i)\bspeakers?\b') {
        return 100
    }

    if ($Name -match '(?i)(internal|integrated|built[- ]?in|onboard)' -and $Name -match '(?i)\bspeakers?\b') {
        return 80
    }

    if ($Name -match '(?i)^Speakers\s*\(High Definition Audio Device\)$') {
        return 70
    }

    if ($Name -match '(?i)^Speakers\b' -and $Name -notmatch $externalRegex) {
        return 50
    }

    return -1
}

function Get-MicrophoneScore {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($Name -notmatch '(?i)(microphone|mic\b|array)') {
        return -1
    }

    $externalRegex = '(?i)(dell|wd19|dock|usb|bluetooth|headset|headphone|earphone|receiver|display|monitor|hdmi|lg|nvidia|audio receiver|line\b|jack|webcam)'

    if ($Name -match $externalRegex) {
        return -1
    }

    if ($Name -match '(?i)microphone array' -and $Name -match '(?i)(intel|realtek|smart sound|digital)') {
        return 100
    }

    if ($Name -match '(?i)realtek') {
        return 90
    }

    if ($Name -match '(?i)(internal|integrated|built[- ]?in|onboard|smart sound|digital)') {
        return 80
    }

    if ($Name -match '(?i)^Microphone\s*\(High Definition Audio Device\)$') {
        return 70
    }

    if ($Name -match '(?i)^Microphone\b' -or $Name -match '(?i)^Microphone Array\b') {
        return 50
    }

    return -1
}

function Get-BestSpeakerEndpoint {
    return @(Get-PlaybackEndpoints |
        Where-Object { $_.Score -ge 0 } |
        Sort-Object -Property @{ Expression = 'Active'; Descending = $true }, @{ Expression = 'Score'; Descending = $true })
}

function Get-BestMicrophoneEndpoint {
    return @(Get-RecordingEndpoints |
        Where-Object { $_.Score -ge 0 } |
        Sort-Object -Property @{ Expression = 'Active'; Descending = $true }, @{ Expression = 'Score'; Descending = $true })
}

function Write-PlaybackList {
    Write-Host ''
    Write-Host 'Playback endpoints Windows reported:'
    foreach ($endpoint in Get-PlaybackEndpoints) {
        $status = if ($endpoint.Active) { 'Active' } else { 'Inactive' }
        $role = if ($endpoint.Score -ge 0) { 'speaker candidate' } else { 'not selected' }
        Write-Host ("  - {0} [{1}; {2}; score {3}]" -f $endpoint.Name, $status, $role, $endpoint.Score)
        Write-Host ("    Endpoint ID: {0}" -f $endpoint.Id)
    }
}

function Write-RecordingList {
    Write-Host ''
    Write-Host 'Recording endpoints Windows reported:'
    foreach ($endpoint in Get-RecordingEndpoints) {
        $status = if ($endpoint.Active) { 'Active' } else { 'Inactive' }
        $role = if ($endpoint.Score -ge 0) { 'laptop microphone candidate' } else { 'not selected' }
        Write-Host ("  - {0} [{1}; {2}; score {3}]" -f $endpoint.Name, $status, $role, $endpoint.Score)
        Write-Host ("    Endpoint ID: {0}" -f $endpoint.Id)
    }
}

function Save-CurrentAudioDefaults {
    $stateDir = Split-Path -Parent $StateFile
    if ($stateDir) {
        New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    }

    $rows = New-Object System.Collections.Generic.List[string]

    foreach ($flow in @([EDataFlow]::eRender, [EDataFlow]::eCapture)) {
        foreach ($role in @([ERole]::eConsole, [ERole]::eMultimedia, [ERole]::eCommunications)) {
            try {
                $id = [AudioDefaults]::GetDefaultEndpointId($flow, $role)
                $rows.Add(("{0}`t{1}`t{2}" -f ([int]$flow), ([int]$role), $id))
            } catch {
                Write-Warning ("Could not save current default for flow {0}, role {1}: {2}" -f $flow, $role, $_.Exception.Message)
            }
        }
    }

    Set-Content -LiteralPath $StateFile -Value $rows
}

function Set-AllRolesForFlow {
    param(
        [Parameter(Mandatory = $true)]
        [EDataFlow] $Flow,

        [Parameter(Mandatory = $true)]
        [string] $EndpointId,

        [Parameter(Mandatory = $true)]
        [string] $EndpointName,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    foreach ($role in @([ERole]::eConsole, [ERole]::eMultimedia, [ERole]::eCommunications)) {
        Write-Host ("Setting {0} default for {1} to {2}" -f $Label, $role, $EndpointName)
        [AudioDefaults]::SetDefaultEndpoint($EndpointId, $role)
    }
}

function Set-CameraTestAudio {
    Write-PlaybackList
    Write-RecordingList
    Save-CurrentAudioDefaults

    $speakerCandidates = @(Get-BestSpeakerEndpoint)
    if ($speakerCandidates.Count -eq 0) {
        Write-Warning 'No laptop speaker endpoint was found. No playback defaults were changed.'
        exit 1
    }

    $speakerSelected = $false
    foreach ($speaker in $speakerCandidates) {
        Write-Host ''
        Write-Host ("Trying speaker output: {0}" -f $speaker.Name)

        try {
            Set-AllRolesForFlow -Flow ([EDataFlow]::eRender) -EndpointId $speaker.Id -EndpointName $speaker.Name -Label 'playback'
            Write-Host ''
            Write-Host ("Selected speaker output: {0}" -f $speaker.Name)
            $speakerSelected = $true
            break
        } catch {
            Write-Warning ("Could not use speaker output {0}: {1}" -f $speaker.Name, $_.Exception.Message)
        }
    }

    if (-not $speakerSelected) {
        Write-Warning 'No speaker output candidate could be selected.'
        exit 1
    }

    $microphoneCandidates = @(Get-BestMicrophoneEndpoint)
    if ($microphoneCandidates.Count -eq 0) {
        Write-Warning 'No built-in laptop microphone endpoint was found. Recording default was not changed.'
        return
    }

    foreach ($microphone in $microphoneCandidates) {
        Write-Host ''
        Write-Host ("Trying laptop microphone input: {0}" -f $microphone.Name)

        try {
            Set-AllRolesForFlow -Flow ([EDataFlow]::eCapture) -EndpointId $microphone.Id -EndpointName $microphone.Name -Label 'recording'
            Write-Host ''
            Write-Host ("Selected microphone input: {0}" -f $microphone.Name)
            return
        } catch {
            Write-Warning ("Could not use microphone input {0}: {1}" -f $microphone.Name, $_.Exception.Message)
        }
    }

    Write-Warning 'No microphone input candidate could be selected.'
}

function Restore-AudioDefaults {
    if (-not (Test-Path -LiteralPath $StateFile)) {
        Write-Host 'No saved audio default list was found.'
        return
    }

    $lines = @(Get-Content -LiteralPath $StateFile | Where-Object { $_.Trim() })
    if ($lines.Count -eq 0) {
        Write-Host 'Saved audio-default file was empty.'
        return
    }

    foreach ($line in $lines) {
        $parts = $line -split "`t", 3

        if ($parts.Count -eq 2) {
            $flow = [EDataFlow]::eRender
            $role = [ERole]([int]$parts[0])
            $endpointId = $parts[1]
        } elseif ($parts.Count -eq 3) {
            $flow = [EDataFlow]([int]$parts[0])
            $role = [ERole]([int]$parts[1])
            $endpointId = $parts[2]
        } else {
            continue
        }

        Write-Host ("Restoring saved default for flow {0}, role {1}" -f $flow, $role)
        [AudioDefaults]::SetDefaultEndpoint($endpointId, $role)
    }

    Remove-Item -LiteralPath $StateFile -Force
}

try {
    if ($Action -eq 'SetSpeaker') {
        Set-CameraTestAudio
    } elseif ($Action -eq 'Restore') {
        Restore-AudioDefaults
    } else {
        Write-PlaybackList
        Write-RecordingList
    }
} finally {
    if ($LogFile) {
        try {
            Stop-Transcript | Out-Null
        } catch {
        }
    }
}
