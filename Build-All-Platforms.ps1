param([string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmmss'))

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Convert-BuildResult {
    param([object[]]$Output)
    $text = ($Output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    $start = $text.LastIndexOf('{')
    if ($start -lt 0) { throw "Build output did not contain a JSON result.`n$text" }
    return $text.Substring($start) | ConvertFrom-Json
}

$v4Json = & (Join-Path $root 'V4\Build-LaptopQAIteration.ps1') -Stamp "$Stamp-AllPlatforms"
if ($LASTEXITCODE -ne 0) { throw "The Windows V4 package build failed with exit code $LASTEXITCODE." }
$v4 = Convert-BuildResult $v4Json

$v5Json = & (Join-Path $root 'V5\Build-LaptopQAIteration.ps1') -Stamp "$Stamp-AllPlatforms"
if ($LASTEXITCODE -ne 0) { throw "The Windows V5 package build failed with exit code $LASTEXITCODE." }
$v5 = Convert-BuildResult $v5Json

dotnet run --project (Join-Path $root 'Mac\LaptopQATestingMac.csproj') -c Release -- --self-test
if ($LASTEXITCODE -ne 0) { throw 'The macOS diagnostics and QA-sheet smoke tests failed.' }

$macJson = & (Join-Path $root 'Mac\Build-MacRelease.ps1') -Stamp "$Stamp-AllPlatforms"
if ($LASTEXITCODE -ne 0) { throw "The macOS package build failed with exit code $LASTEXITCODE." }
$mac = Convert-BuildResult $macJson

$macApps = @([pscustomobject]@{ Name = 'macOS Laptop QA Launcher.app'; Source = [string]$mac.App; Architecture = 'Apple Silicon' })
foreach ($app in $macApps) {
    if (-not (Test-Path -LiteralPath (Join-Path $app.Source 'Contents\MacOS\LaptopQATestingMac') -PathType Leaf)) {
        throw "The $($app.Architecture) directly runnable macOS app was not built correctly: $($app.Source)"
    }
}

foreach ($packageRoot in @([string]$v4.IterationFolder, [string]$v5.IterationFolder)) {
    $packageMacRoot = Join-Path $packageRoot 'macOS'
    if (Test-Path -LiteralPath $packageMacRoot -PathType Container) {
        $resolvedPackageMacRoot = (Resolve-Path -LiteralPath $packageMacRoot).Path
        $expectedPackageMacRoot = Join-Path $packageRoot 'macOS'
        if (-not [string]::Equals($resolvedPackageMacRoot.TrimEnd('\'), $expectedPackageMacRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected obsolete package macOS folder: $resolvedPackageMacRoot"
        }
        Remove-Item -LiteralPath $resolvedPackageMacRoot -Recurse -Force
    }
    foreach ($app in $macApps) {
        robocopy $app.Source (Join-Path $packageRoot $app.Name) /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Could not add the $($app.Architecture) app to $packageRoot. Robocopy exit code: $LASTEXITCODE" }
    }
    Remove-Item -LiteralPath (Join-Path $packageRoot 'Open on macOS.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageRoot 'Open Laptop QA on macOS.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $packageRoot 'Start Laptop QA.command') -Force -ErrorAction SilentlyContinue
}

$removableDeployment = [System.IO.DriveInfo]::GetDrives() |
    Where-Object { $_.IsReady -and $_.DriveType -eq [System.IO.DriveType]::Removable } |
    ForEach-Object {
        $driveRoot = $_.RootDirectory.FullName
        $targetRoot = Join-Path $driveRoot 'LAPTOP QA'
        if (Test-Path -LiteralPath (Join-Path $targetRoot 'App\LaptopQATestingV4.exe') -PathType Leaf) {
            [pscustomobject]@{ DriveRoot = $driveRoot; TargetRoot = $targetRoot; VolumeName = [string]$_.VolumeLabel }
        }
    } |
    Sort-Object @{ Expression = { if ($_.VolumeName -eq 'IT SUPP') { 0 } else { 1 } } }, DriveRoot |
    Select-Object -First 1

$removableUniversal = $null
if ($null -ne $removableDeployment) {
    $resolvedTargetRoot = (Resolve-Path -LiteralPath $removableDeployment.TargetRoot).Path
    $expectedTargetRoot = Join-Path $removableDeployment.DriveRoot 'LAPTOP QA'
    if (-not [string]::Equals($resolvedTargetRoot.TrimEnd('\'), $expectedTargetRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected removable-drive target: $resolvedTargetRoot"
    }

    $obsoleteMacTarget = Join-Path $resolvedTargetRoot 'macOS'
    if (Test-Path -LiteralPath $obsoleteMacTarget -PathType Container) {
        $resolvedObsoleteMacTarget = (Resolve-Path -LiteralPath $obsoleteMacTarget).Path
        $expectedObsoleteMacTarget = Join-Path $resolvedTargetRoot 'macOS'
        if (-not [string]::Equals($resolvedObsoleteMacTarget.TrimEnd('\'), $expectedObsoleteMacTarget.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected obsolete macOS package target: $resolvedObsoleteMacTarget"
        }
        Remove-Item -LiteralPath $resolvedObsoleteMacTarget -Recurse -Force
    }

    foreach ($obsoleteAppName in @('Laptop QA.app', 'Laptop QA - Apple Silicon.app', 'Laptop QA - Intel.app')) {
        $obsoleteApp = Join-Path $removableDeployment.DriveRoot $obsoleteAppName
        if (Test-Path -LiteralPath $obsoleteApp -PathType Container) {
            $resolvedObsoleteApp = (Resolve-Path -LiteralPath $obsoleteApp).Path
            $expectedObsoleteApp = Join-Path $removableDeployment.DriveRoot $obsoleteAppName
            if (-not [string]::Equals($resolvedObsoleteApp.TrimEnd('\'), $expectedObsoleteApp.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Unexpected obsolete macOS app target: $resolvedObsoleteApp"
            }
            Remove-Item -LiteralPath $resolvedObsoleteApp -Recurse -Force
        }
    }

    foreach ($app in $macApps) {
        $targetApp = Join-Path $removableDeployment.DriveRoot $app.Name
        robocopy $app.Source $targetApp /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -gt 7) { throw "Could not copy the $($app.Architecture) macOS app to the removable drive. Robocopy exit code: $LASTEXITCODE" }
    }

    Remove-Item -LiteralPath (Join-Path $removableDeployment.DriveRoot 'Open Laptop QA on macOS.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $removableDeployment.DriveRoot 'Open on macOS.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $removableDeployment.DriveRoot 'Start Laptop QA.command') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $removableDeployment.DriveRoot 'Start Laptop QA.cmd') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $root 'Start-LaptopQA-Silent.vbs') -Destination (Join-Path $removableDeployment.DriveRoot 'Windows Laptop QA Launcher.vbs') -Force
    Remove-Item -LiteralPath (Join-Path $removableDeployment.DriveRoot 'Laptop QA.vbs') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $root 'Start-LaptopQA-Local.ps1') -Destination (Join-Path $resolvedTargetRoot 'App\Start Laptop QA Local.ps1') -Force

    $v4SourceHash = (Get-FileHash -LiteralPath (Join-Path $v4.AppFolder 'LaptopQATestingV4.exe') -Algorithm SHA256).Hash
    $v4TargetHash = (Get-FileHash -LiteralPath (Join-Path $resolvedTargetRoot 'App\LaptopQATestingV4.exe') -Algorithm SHA256).Hash
    if ($v4SourceHash -ne $v4TargetHash) { throw 'The removable-drive V4 copy did not verify after the universal update.' }
    foreach ($app in $macApps) {
        $macExe = Join-Path $removableDeployment.DriveRoot "$($app.Name)\Contents\MacOS\LaptopQATestingMac"
        if (-not (Test-Path -LiteralPath $macExe -PathType Leaf)) { throw "The removable-drive $($app.Architecture) macOS app was not verified." }
        $sourceMacExe = Join-Path $app.Source 'Contents\MacOS\LaptopQATestingMac'
        $sourceMacHash = (Get-FileHash -LiteralPath $sourceMacExe -Algorithm SHA256).Hash
        $targetMacHash = (Get-FileHash -LiteralPath $macExe -Algorithm SHA256).Hash
        if ($sourceMacHash -ne $targetMacHash) { throw "The removable-drive $($app.Architecture) macOS app did not match the release build." }
    }
    $removableUniversal = "Windows V4 and the directly runnable Apple Silicon macOS app updated and verified: $($removableDeployment.DriveRoot)"
}

[pscustomobject]@{
    Stamp = $Stamp
    V4 = $v4.IterationFolder
    V5 = $v5.IterationFolder
    Mac = $mac.ReleaseFolder
    Removable = $removableUniversal
} | ConvertTo-Json
