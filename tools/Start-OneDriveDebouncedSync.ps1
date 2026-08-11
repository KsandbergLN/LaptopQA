param(
    [Parameter(Mandatory = $true)][string]$SourceFolder,
    [Parameter(Mandatory = $true)][string]$VersionPrefix,
    [int]$DelayMinutes = 60,
    [switch]$Worker,
    [string]$Token = ""
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$pendingDir = Join-Path $repoRoot '.onedrive-sync'
$pendingFile = Join-Path $pendingDir "$VersionPrefix.pending.json"
$logFile = Join-Path $pendingDir "$VersionPrefix.sync.log"

function Write-SyncLog {
    param([string]$Message)
    New-Item -ItemType Directory -Force -Path $pendingDir | Out-Null
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message" | Add-Content -LiteralPath $logFile
}

function Get-OneDriveRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:OneDriveCommercial) -and (Test-Path -LiteralPath $env:OneDriveCommercial -PathType Container)) {
        return $env:OneDriveCommercial
    }

    if (-not [string]::IsNullOrWhiteSpace($env:OneDrive) -and (Test-Path -LiteralPath $env:OneDrive -PathType Container)) {
        return $env:OneDrive
    }

    return $null
}

function Remove-DirectoryRobust {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove folder outside the expected OneDrive root: $Path"
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return
    }
    catch {
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                try { $_.Attributes = [System.IO.FileAttributes]::Normal } catch { }
            }
        try {
            (Get-Item -LiteralPath $Path -Force).Attributes = [System.IO.FileAttributes]::Directory
        }
        catch { }

        $fullPath = [System.IO.Path]::GetFullPath($Path)
        $longPath = if ($fullPath.StartsWith('\\?\')) {
            $fullPath
        }
        elseif ($fullPath.StartsWith('\\')) {
            '\\?\UNC\' + $fullPath.Substring(2)
        }
        else {
            '\\?\' + $fullPath
        }
        [System.IO.Directory]::Delete($longPath, $true)
    }
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AllowedRoot
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $Path"
    }
}

function Sync-OneDriveIteration {
    param(
        [Parameter(Mandatory = $true)][string]$Folder,
        [Parameter(Mandatory = $true)][string]$Prefix,
        [Parameter(Mandatory = $true)][string]$OneDriveRoot
    )

    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
        throw "Source iteration folder no longer exists: $Folder"
    }

    $iterationName = Split-Path -Leaf $Folder
    $destination = Join-Path $OneDriveRoot "$iterationName.zip"
    $partialDestination = "$destination.partial"
    $tempArchive = Join-Path $pendingDir "$iterationName.$([guid]::NewGuid().ToString('N')).zip"

    Assert-PathWithinRoot -Path $destination -AllowedRoot $OneDriveRoot
    Assert-PathWithinRoot -Path $partialDestination -AllowedRoot $OneDriveRoot
    Assert-PathWithinRoot -Path $tempArchive -AllowedRoot $pendingDir

    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $Folder,
            $tempArchive,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false
        )

        $sourceFileCount = (Get-ChildItem -LiteralPath $Folder -Recurse -File -Force | Measure-Object).Count
        $archive = [System.IO.Compression.ZipFile]::OpenRead($tempArchive)
        try {
            $archiveFileCount = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) }).Count
        }
        finally {
            $archive.Dispose()
        }

        if ($sourceFileCount -lt 1 -or $archiveFileCount -ne $sourceFileCount) {
            throw "ZIP verification failed for $iterationName. Source files: $sourceFileCount; archived files: $archiveFileCount."
        }

        Remove-Item -LiteralPath $partialDestination -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath $tempArchive -Destination $partialDestination -Force
        Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
        Move-Item -LiteralPath $partialDestination -Destination $destination -Force
    }
    finally {
        Remove-Item -LiteralPath $tempArchive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $partialDestination -Force -ErrorAction SilentlyContinue
    }

    Get-ChildItem -LiteralPath $OneDriveRoot -Directory -Filter "$Prefix-Iteration-*" -ErrorAction SilentlyContinue |
        ForEach-Object {
            $oldFolderPath = $_.FullName
            try {
                Remove-DirectoryRobust -Path $oldFolderPath -AllowedRoot $OneDriveRoot
            }
            catch {
                Write-SyncLog "$Prefix ZIP uploaded, but an older loose folder could not be removed yet: $oldFolderPath. $($_.Exception.Message)"
            }
        }

    Get-ChildItem -LiteralPath $OneDriveRoot -File -Filter "$Prefix-Iteration-*.zip" -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::Equals($_.FullName, $destination, [System.StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object {
            $oldZipPath = $_.FullName
            try {
                Assert-PathWithinRoot -Path $oldZipPath -AllowedRoot $OneDriveRoot
                Remove-Item -LiteralPath $oldZipPath -Force
            }
            catch {
                Write-SyncLog "$Prefix ZIP uploaded, but an older ZIP could not be removed yet: $oldZipPath. $($_.Exception.Message)"
            }
        }

    return $destination
}

New-Item -ItemType Directory -Force -Path $pendingDir | Out-Null

if (-not $Worker) {
    $oneDriveRoot = Get-OneDriveRoot
    if ([string]::IsNullOrWhiteSpace($oneDriveRoot)) {
        Write-SyncLog "$VersionPrefix sync skipped because OneDrive root was not found."
        return $null
    }

    $newToken = [guid]::NewGuid().ToString('N')
    $dueUtc = [DateTime]::UtcNow.AddMinutes($DelayMinutes)
    [pscustomobject]@{
        VersionPrefix = $VersionPrefix
        SourceFolder = [System.IO.Path]::GetFullPath($SourceFolder)
        OneDriveRoot = [System.IO.Path]::GetFullPath($oneDriveRoot)
        Token = $newToken
        DueUtc = $dueUtc.ToString('o')
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $pendingFile -Encoding UTF8

    $powershell = (Get-Process -Id $PID).Path
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-SourceFolder', "`"$SourceFolder`"",
        '-VersionPrefix', "`"$VersionPrefix`"",
        '-DelayMinutes', $DelayMinutes,
        '-Worker',
        '-Token', "`"$newToken`""
    )
    Start-Process -FilePath $powershell -ArgumentList $args -WindowStyle Hidden | Out-Null

    $destination = Join-Path $oneDriveRoot "$(Split-Path -Leaf $SourceFolder).zip"
    Write-SyncLog "$VersionPrefix scheduled for $($dueUtc.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')): $SourceFolder -> $destination"
    return "Scheduled for $($dueUtc.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')): $destination"
}

try {
    Start-Sleep -Seconds ([Math]::Max(0, $DelayMinutes * 60))

    if (-not (Test-Path -LiteralPath $pendingFile -PathType Leaf)) {
        Write-SyncLog "$VersionPrefix worker $Token exited because no pending sync file exists."
        return
    }

    $pending = Get-Content -LiteralPath $pendingFile -Raw | ConvertFrom-Json
    if ($pending.Token -ne $Token) {
        Write-SyncLog "$VersionPrefix worker $Token exited because a newer iteration is pending."
        return
    }

    $dueUtc = [DateTime]::Parse($pending.DueUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    $remaining = [int][Math]::Ceiling(($dueUtc - [DateTime]::UtcNow).TotalSeconds)
    if ($remaining -gt 0) {
        Start-Sleep -Seconds $remaining
    }

    $latest = Get-Content -LiteralPath $pendingFile -Raw | ConvertFrom-Json
    if ($latest.Token -ne $Token) {
        Write-SyncLog "$VersionPrefix worker $Token exited because a newer iteration arrived during the final wait."
        return
    }

    $destination = Sync-OneDriveIteration -Folder $latest.SourceFolder -Prefix $latest.VersionPrefix -OneDriveRoot $latest.OneDriveRoot
    Remove-Item -LiteralPath $pendingFile -Force -ErrorAction SilentlyContinue
    Write-SyncLog "$VersionPrefix synced to OneDrive: $destination"
}
catch {
    Write-SyncLog "$VersionPrefix sync failed: $($_.Exception.Message)"
}
