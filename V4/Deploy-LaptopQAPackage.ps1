[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [switch]$OneDrive,
    [switch]$RemovableDrives
)

$ErrorActionPreference = 'Stop'
if (-not $OneDrive -and -not $RemovableDrives) {
    throw 'Choose at least one explicit deployment target: -OneDrive or -RemovableDrives.'
}

$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $package 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Package manifest not found: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Status -ne 'Accepted') {
    throw "Only an Accepted package can be deployed. Current status: $($manifest.Status)"
}

foreach ($entry in $manifest.Files) {
    $file = Join-Path $package ([string]$entry.Path)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Accepted package is incomplete: $($entry.Path)"
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne [string]$entry.SHA256) {
        throw "Accepted package hash mismatch: $($entry.Path)"
    }
}

$handoffRoot = Split-Path -Parent $PSScriptRoot
$appFolder = Join-Path $package 'LAPTOP QA\App'
$sourceExe = Join-Path $appFolder 'LaptopQATestingV4.exe'
$silentLauncher = Join-Path $package 'Windows Laptop QA Launcher.vbs'
$driveMarker = Join-Path $package 'Laptop-QA-Drive.json'
$results = @()

if ($OneDrive) {
    $syncScript = Join-Path $handoffRoot 'tools\Start-OneDriveDebouncedSync.ps1'
    if (-not (Test-Path -LiteralPath $syncScript -PathType Leaf)) {
        throw "OneDrive deployment helper not found: $syncScript"
    }
    if ($PSCmdlet.ShouldProcess('OneDrive release location', "Deploy accepted package $package")) {
        $results += & $syncScript -SourceFolder $package -VersionPrefix 'LaptopQATestingV4' -DelayMinutes 30
    }
}

if ($RemovableDrives) {
    $targets = @([System.IO.DriveInfo]::GetDrives() |
        Where-Object { $_.IsReady -and $_.DriveType -eq [System.IO.DriveType]::Removable } |
        ForEach-Object {
            $targetRoot = Join-Path $_.RootDirectory.FullName 'LAPTOP QA'
            $targetApp = Join-Path $targetRoot 'App'
            $targetExe = Join-Path $targetApp 'LaptopQATestingV4.exe'
            if (Test-Path -LiteralPath $targetExe -PathType Leaf) {
                [pscustomobject]@{
                    DriveRoot = $_.RootDirectory.FullName
                    TargetRoot = $targetRoot
                    TargetApp = $targetApp
                    TargetExe = $targetExe
                    VolumeName = [string]$_.VolumeLabel
                }
            }
        })

    foreach ($target in $targets) {
        $resolvedTargetApp = (Resolve-Path -LiteralPath $target.TargetApp).Path
        $expectedPrefix = Join-Path $target.DriveRoot 'LAPTOP QA\App'
        if (-not $resolvedTargetApp.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected removable-drive target: $resolvedTargetApp"
        }

        if ($PSCmdlet.ShouldProcess($target.DriveRoot, "Deploy accepted package $($manifest.PackageName)")) {
            $targetConfig = Join-Path $target.TargetRoot 'Laptop-QA-Config.json'
            $configHashBefore = if (Test-Path -LiteralPath $targetConfig -PathType Leaf) {
                (Get-FileHash -LiteralPath $targetConfig -Algorithm SHA256).Hash
            }

            Copy-Item -Path (Join-Path $appFolder '*') -Destination $resolvedTargetApp -Recurse -Force
            Copy-Item -LiteralPath $silentLauncher -Destination (Join-Path $target.DriveRoot 'Windows Laptop QA Launcher.vbs') -Force
            Copy-Item -LiteralPath $driveMarker -Destination (Join-Path $target.DriveRoot 'Laptop-QA-Drive.json') -Force

            if ($null -ne $configHashBefore) {
                $configHashAfter = (Get-FileHash -LiteralPath $targetConfig -Algorithm SHA256).Hash
                if ($configHashBefore -ne $configHashAfter) {
                    throw "Deployment changed the configuration on $($target.DriveRoot)."
                }
            }
            if ((Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target.TargetExe -Algorithm SHA256).Hash) {
                throw "Executable verification failed on $($target.DriveRoot)."
            }
            $results += "Updated and verified $($target.DriveRoot)"
        }
    }
}

[pscustomobject]@{
    Package = $package
    Targets = @($results)
    WhatIf = [bool]$WhatIfPreference
} | ConvertTo-Json -Depth 3
