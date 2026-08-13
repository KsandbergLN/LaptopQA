param(
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

$scriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

if (-not $isWindowsPlatform) {
    if (-not $isMacPlatform) {
        throw 'Laptop QA supports Windows and macOS only.'
    }

    $macPackageRoot = if ((Split-Path -Leaf $scriptFolder) -eq 'App') { Split-Path -Parent $scriptFolder } else { $scriptFolder }
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($architecture -ne 'arm64') { throw "Laptop QA for macOS supports Apple Silicon only. Detected architecture: $architecture" }
    $sourceMacApp = Join-Path (Split-Path -Parent $macPackageRoot) 'macOS Laptop QA Launcher.app'
    if (-not (Test-Path -LiteralPath $sourceMacApp -PathType Container)) {
        $sourceMacApp = Join-Path $macPackageRoot 'macOS/macOS Laptop QA Launcher.app'
    }
    if (-not (Test-Path -LiteralPath $sourceMacApp -PathType Container)) {
        $sourceMacApp = Join-Path (Split-Path -Parent $macPackageRoot) 'Laptop QA.app'
    }
    if (-not (Test-Path -LiteralPath $sourceMacApp -PathType Container)) {
        $sourceMacApp = Join-Path $macPackageRoot 'macOS/Laptop QA.app'
    }
    if (-not (Test-Path -LiteralPath $sourceMacApp -PathType Container)) {
        throw "The Apple Silicon Laptop QA app was not found:`n$sourceMacApp"
    }

    $sourceMacExe = Join-Path $sourceMacApp 'Contents/MacOS/LaptopQA.Mac'
    $sourceMacInfo = Get-Item -LiteralPath $sourceMacExe
    $macStamp = "{0}-{1}" -f $sourceMacInfo.LastWriteTimeUtc.ToString('yyyyMMddHHmmss'), $sourceMacInfo.Length
    $macRuntimeRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) "Laptop QA/Runtime/$macStamp"
    $localMacApp = Join-Path $macRuntimeRoot 'macOS Laptop QA Launcher.app'
    New-Item -ItemType Directory -Force -Path $macRuntimeRoot | Out-Null
    if (-not (Test-Path -LiteralPath $localMacApp -PathType Container)) {
        & /usr/bin/ditto $sourceMacApp $localMacApp
        if ($LASTEXITCODE -ne 0) { throw "Could not stage the macOS app locally. ditto exit code: $LASTEXITCODE" }
    }
    & /bin/chmod +x (Join-Path $localMacApp 'Contents/MacOS/LaptopQA.Mac')
    & /usr/bin/open $localMacApp --args --data-root $macPackageRoot
    if ($LASTEXITCODE -ne 0) { throw "macOS could not open Laptop QA. open exit code: $LASTEXITCODE" }
    exit 0
}

$exeName = 'LaptopQATestingV4.exe'
$isInsideAppFolder = Test-Path -LiteralPath (Join-Path $scriptFolder $exeName) -PathType Leaf
$nestedAppFolder = Join-Path $scriptFolder 'App'
$isContainerFolder = Test-Path -LiteralPath (Join-Path $nestedAppFolder $exeName) -PathType Leaf
$packageRoot = if ($isInsideAppFolder) { Split-Path -Parent $scriptFolder } else { $scriptFolder }
$sourceApp = if ($isInsideAppFolder) {
    $scriptFolder
} elseif ($isContainerFolder) {
    $nestedAppFolder
} elseif (Test-Path -LiteralPath (Join-Path $packageRoot (Join-Path 'LAPTOP QA\App' $exeName)) -PathType Leaf) {
    Join-Path $packageRoot 'LAPTOP QA\App'
} else {
    Join-Path $packageRoot 'LAPTOP QA'
}
function Get-CachedLaptopIdentifier {
    $cachePath = Join-Path $packageRoot '.runtime\qa-session.json'
    if (Test-Path -LiteralPath $cachePath -PathType Leaf) {
        try {
            $cache = Get-Content -LiteralPath $cachePath -Raw -Encoding UTF8 | ConvertFrom-Json
            $candidates = @(
                $cache.ServiceTag,
                $cache.Hardware.BiosSerialNumber,
                $cache.Hardware.ChassisSerial,
                $cache.AssetTag,
                $cache.Hardware.Computer
            )
            foreach ($candidate in $candidates) {
                $value = [string]$candidate
                if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Trim() -notmatch '^(unknown|unavailable|not set|n/?a|none)$') {
                    return ($value.Trim() -replace '[\\/:*?"<>|]', '-')
                }
            }
        }
        catch {
        }
    }
    return 'Laptop'
}

$launcherIdentifier = Get-CachedLaptopIdentifier
$launcherLog = Join-Path $packageRoot ("logs\{0}-{1}-Launcher.log" -f $launcherIdentifier, (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$dataItems = @('.runtime', 'hardware', 'hash', 'logs', 'QA sheets', 'Laptop-QA-Config.json')
$localRoot = $null

function Write-LauncherLog {
    param([string]$Message)

    try {
        $logDir = Split-Path -Parent $launcherLog
        New-Item -ItemType Directory -Force -Path $logDir | Out-Null
        Add-Content -LiteralPath $launcherLog -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message) -Encoding UTF8
    } catch {
    }
}

function Show-Error {
    param([string]$Message)
    Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue
    [System.Windows.MessageBox]::Show($Message, 'Laptop QA', 'OK', 'Error') | Out-Null
}

function Copy-AppFolder {
    param(
        [string]$Source,
        [string]$Destination
    )

    $existingLocalExe = Join-Path $Destination $exeName
    if (Test-Path -LiteralPath $existingLocalExe -PathType Leaf) {
        Write-LauncherLog "Using existing local staged copy: $Destination"
        Remove-LocalDataItems -LocalApp $Destination
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    robocopy $Source $Destination /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XD '.runtime' 'hardware' 'hash' 'logs' 'QA sheets' /XF 'Laptop-QA-Config.json' 'Start Laptop QA Local.ps1' | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "Copy failed from $Source to $Destination. Robocopy exit code: $LASTEXITCODE"
    }
    Remove-LocalDataItems -LocalApp $Destination
}

function Remove-LocalDataItems {
    param([string]$LocalApp)

    foreach ($item in $dataItems) {
        $localItem = Join-Path $LocalApp $item
        try {
            if (Test-Path -LiteralPath $localItem) {
                Remove-Item -LiteralPath $localItem -Recurse -Force
            }
        }
        catch {
            Write-LauncherLog "Could not remove local data item ${item}: $($_.Exception.Message)"
        }
    }
}

function Remove-EmptyFolderIfPossible {
    param(
        [string]$Path,
        [string]$Label
    )

    try {
        if ([string]::IsNullOrWhiteSpace($Path)) { return }
        if ((Test-Path -LiteralPath $Path -PathType Container) -and -not (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1)) {
            Remove-Item -LiteralPath $Path -Force
            Write-LauncherLog "Removed ${Label}: $Path"
        }
    }
    catch {
        Write-LauncherLog "Could not remove ${Label}: $($_.Exception.Message)"
    }
}

function Remove-EmptyChildFoldersIfPossible {
    param(
        [string]$Path,
        [string]$Label
    )

    try {
        if ([string]::IsNullOrWhiteSpace($Path)) { return }
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
        Get-ChildItem -LiteralPath $Path -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-EmptyFolderIfPossible -Path $_.FullName -Label $Label
        }
    }
    catch {
        Write-LauncherLog "Could not scan local cleanup folder ${Path}: $($_.Exception.Message)"
    }
}

function Remove-OldLocalStageFolders {
    param(
        [string]$LocalBase,
        [string]$CurrentLocalRoot
    )

    try {
        if ([string]::IsNullOrWhiteSpace($LocalBase)) { return }
        if (-not (Test-Path -LiteralPath $LocalBase -PathType Container)) { return }

        $currentFull = ''
        if (-not [string]::IsNullOrWhiteSpace($CurrentLocalRoot)) {
            $currentFull = [System.IO.Path]::GetFullPath($CurrentLocalRoot).TrimEnd('\', '/')
        }

        Get-ChildItem -LiteralPath $LocalBase -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $versionRoot = $_.FullName
            Get-ChildItem -LiteralPath $versionRoot -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
                $candidate = [System.IO.Path]::GetFullPath($_.FullName).TrimEnd('\', '/')
                if ($currentFull -and [string]::Equals($candidate, $currentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return
                }

                try {
                    Remove-Item -LiteralPath $candidate -Recurse -Force
                    Write-LauncherLog "Removed old local staged folder: $candidate"
                }
                catch {
                    Write-LauncherLog "Could not remove old local staged folder ${candidate}: $($_.Exception.Message)"
                }
            }

            Remove-EmptyFolderIfPossible -Path $versionRoot -Label 'empty local version folder'
        }

        Remove-EmptyFolderIfPossible -Path $LocalBase -Label 'empty local Laptop QA folder'
    }
    catch {
        Write-LauncherLog "Could not clean old local staged folders: $($_.Exception.Message)"
    }
}

function Remove-LocalStagingFolder {
    param([string]$LocalRoot)

    $removed = $false
    $lastError = $null
    Start-Sleep -Milliseconds 250
    for ($attempt = 1; $attempt -le 15; $attempt++) {
        try {
            if (Test-Path -LiteralPath $LocalRoot) {
                Remove-Item -LiteralPath $LocalRoot -Recurse -Force
                Write-LauncherLog "Removed local staged folder: $LocalRoot"
            }
            $removed = $true
            break
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds ([Math]::Min(1500, 250 * $attempt))
        }
    }

    try {
        $versionRoot = Split-Path -Parent $LocalRoot
        $localBase = if ($versionRoot) { Split-Path -Parent $versionRoot } else { $null }
        if ($removed) {
            Remove-OldLocalStageFolders -LocalBase $localBase -CurrentLocalRoot $LocalRoot
            Remove-EmptyFolderIfPossible -Path $versionRoot -Label 'empty local version folder'
            Remove-EmptyChildFoldersIfPossible -Path $localBase -Label 'empty local version folder'
            Remove-EmptyFolderIfPossible -Path $localBase -Label 'empty local Laptop QA folder'
        }
    }
    catch {
        Write-LauncherLog "Could not remove local staged folder: $($_.Exception.Message)"
    }

    if (-not $removed -and $lastError) {
        Write-LauncherLog "Could not remove local staged folder after retries: $lastError"
    }
}

try {
    Write-LauncherLog "Startup helper launched from $packageRoot"
    if (-not (Test-Path -LiteralPath $sourceApp)) {
        throw "The LAPTOP QA folder was not found next to this start script.`n`nExpected:`n$sourceApp"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $sourceApp $exeName) -PathType Leaf)) {
        throw "Could not find $exeName inside:`n$sourceApp"
    }
    $sourceExe = Join-Path $sourceApp $exeName
    $sourceStampFile = Join-Path $sourceApp ($exeName -replace '\.exe$', '.dll')
    if (-not (Test-Path -LiteralPath $sourceStampFile)) {
        $sourceStampFile = $sourceExe
    }
    $sourceStampInfo = Get-Item -LiteralPath $sourceStampFile
    $packageStamp = "{0}-{1}" -f $sourceStampInfo.LastWriteTimeUtc.ToString('yyyyMMddHHmmss'), $sourceStampInfo.Length
    $localRoot = Join-Path $env:LOCALAPPDATA "Laptop QA\$version\$packageStamp"
    $localApp = Join-Path $localRoot 'LAPTOP QA'
    $localExe = Join-Path $localApp $exeName

    Copy-AppFolder -Source $sourceApp -Destination $localApp
    Write-LauncherLog "Copied app to local folder: $localApp"

    if (-not (Test-Path -LiteralPath $localExe -PathType Leaf)) {
        throw "The local staged executable was not created:`n$localExe"
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $localExe
    $startInfo.WorkingDirectory = $localApp
    $startInfo.UseShellExecute = $true
    $escapedDataRoot = $packageRoot.Replace('"', '\"')
    $startInfo.Arguments = "--data-root `"$escapedDataRoot`""
    $process = [System.Diagnostics.Process]::Start($startInfo)
    Write-LauncherLog "Started local app: $localExe"
    Write-LauncherLog "App data root passed as removable folder: $packageRoot"
    if (-not $NoWait) {
        $process.WaitForExit()
        Write-LauncherLog "Local app exited with code $($process.ExitCode)"
        Remove-LocalStagingFolder -LocalRoot $localRoot
    }
}
catch {
    Write-LauncherLog "Startup helper failed: $($_.Exception.Message)"
    if ($localRoot) {
        Remove-LocalStagingFolder -LocalRoot $localRoot
    }
    Show-Error $_.Exception.Message
    exit 1
}
