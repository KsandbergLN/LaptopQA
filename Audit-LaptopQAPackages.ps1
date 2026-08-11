[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$QuarantineIncomplete
)

$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
$quarantine = Join-Path $dist 'quarantine'
New-Item -ItemType Directory -Force -Path $quarantine | Out-Null
$distResolved = (Resolve-Path -LiteralPath $dist).Path
$results = @()

foreach ($folder in Get-ChildItem -LiteralPath $dist -Directory |
    Where-Object { $_.Name -notin @('quarantine', '.staging') } |
    Sort-Object LastWriteTime -Descending) {
    $appExe = Join-Path $folder.FullName 'LAPTOP QA\App\LaptopQATestingV4.exe'
    $hasConfig = (Test-Path -LiteralPath (Join-Path $folder.FullName 'LAPTOP QA\Laptop-QA-Config.json') -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $folder.FullName 'LAPTOP QA\App\Laptop-QA-Config.json') -PathType Leaf)
    $hasLauncher = (Test-Path -LiteralPath (Join-Path $folder.FullName 'Windows Laptop QA Launcher.vbs') -PathType Leaf) -or
        (Test-Path -LiteralPath (Join-Path $folder.FullName 'Laptop QA.vbs') -PathType Leaf)
    $missing = @()
    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) { $missing += 'LaptopQATestingV4.exe' }
    if (-not $hasConfig) { $missing += 'Laptop-QA-Config.json' }
    if (-not $hasLauncher) { $missing += 'launcher' }
    $manifestPath = Join-Path $folder.FullName 'package-manifest.json'
    $status = if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
        'Incomplete'
    } elseif (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).Status
    } elseif ($missing.Count -gt 0) {
        'Legacy-Nonconforming'
    } else {
        'Legacy-Unmanifested'
    }

    $destination = $null
    if ($status -eq 'Incomplete' -and $QuarantineIncomplete) {
        $resolved = (Resolve-Path -LiteralPath $folder.FullName).Path
        if (-not $resolved.StartsWith($distResolved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to move a folder outside dist: $resolved"
        }
        $destination = Join-Path $quarantine $folder.Name
        if (Test-Path -LiteralPath $destination) {
            $destination = Join-Path $quarantine "$($folder.Name)-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        }
        if ($PSCmdlet.ShouldProcess($resolved, "Move incomplete package to $destination")) {
            Move-Item -LiteralPath $resolved -Destination $destination
        }
    }

    $results += [pscustomobject]@{
        Package = $folder.Name
        Status = $status
        MissingCount = $missing.Count
        Manifest = Test-Path -LiteralPath $manifestPath -PathType Leaf
        QuarantineDestination = $destination
    }
}

$results
