param([string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmmss'))

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'LaptopQA.Mac.csproj'
$dist = Join-Path $projectDir "dist\LaptopQA.Mac-$Stamp"
$staging = Join-Path $dist '.publish'
$rid = 'osx-arm64'
$appRoot = Join-Path $dist 'macOS Laptop QA Launcher.app'
$driveMarkerSource = Join-Path (Split-Path -Parent $projectDir) 'Laptop-QA-Drive.json'
$driveMarkerTarget = Join-Path $dist 'Laptop-QA-Drive.json'

$publish = Join-Path $staging $rid
dotnet publish $project -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:UseSharedCompilation=false `
    -p:BuildInParallel=false `
    -p:ConcurrentBuild=false `
    -p:RunAnalyzers=false `
    -m:1 `
    -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$app = Join-Path $appRoot 'Contents'
New-Item -ItemType Directory -Force -Path (Join-Path $app 'MacOS'), (Join-Path $app 'Resources') | Out-Null
Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Name -notin @('LaptopQA.Mac', 'LaptopQA.Mac.dSYM') } | Copy-Item -Destination (Join-Path $app 'MacOS') -Force
if (Test-Path (Join-Path $publish 'LaptopQA.Mac')) { Copy-Item (Join-Path $publish 'LaptopQA.Mac') (Join-Path $app 'MacOS\LaptopQA.Mac') -Force }
Copy-Item (Join-Path $projectDir 'Assets\app-icon.png') (Join-Path $app 'Resources\app-icon.png') -Force
Copy-Item (Join-Path $projectDir 'Assets\app-icon.icns') (Join-Path $app 'Resources\app-icon.icns') -Force
Copy-Item (Join-Path $projectDir 'Info.plist') (Join-Path $app 'Info.plist') -Force
if (Test-Path -LiteralPath $driveMarkerSource -PathType Leaf) {
    Copy-Item -LiteralPath $driveMarkerSource -Destination $driveMarkerTarget -Force
}

if (Test-Path -LiteralPath $staging) {
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path
    if (-not $resolvedStaging.StartsWith(($resolvedDist.TrimEnd('\') + '\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected macOS publish staging path: $resolvedStaging"
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
Remove-Item -LiteralPath (Join-Path $dist 'Open on macOS.txt') -Force -ErrorAction SilentlyContinue

$removableDriveCopies = @()
$removableDrives = @([System.IO.DriveInfo]::GetDrives() | Where-Object {
    $_.IsReady -and
    $_.DriveType -eq [System.IO.DriveType]::Removable -and
    ($_.VolumeLabel -eq 'IT SUPP' -or (Test-Path -LiteralPath (Join-Path $_.RootDirectory.FullName 'LAPTOP QA\App\LaptopQA.Windows.exe') -PathType Leaf))
})
foreach ($drive in $removableDrives) {
    $driveRoot = (Resolve-Path -LiteralPath $drive.RootDirectory.FullName).Path.TrimEnd('\')
    $targetApp = Join-Path $driveRoot 'macOS Laptop QA Launcher.app'
    if (-not $targetApp.StartsWith(($driveRoot + '\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected removable-drive macOS target: $targetApp"
    }
    if (Test-Path -LiteralPath $targetApp) {
        Remove-Item -LiteralPath $targetApp -Recurse -Force
    }
    Copy-Item -LiteralPath $appRoot -Destination $targetApp -Recurse -Force
    if (Test-Path -LiteralPath $driveMarkerSource -PathType Leaf) {
        Copy-Item -LiteralPath $driveMarkerSource -Destination (Join-Path $driveRoot 'Laptop-QA-Drive.json') -Force
    }
    Remove-Item -LiteralPath (Join-Path $driveRoot 'Open on macOS.txt') -Force -ErrorAction SilentlyContinue

    $sourceExecutable = Join-Path $appRoot 'Contents\MacOS\LaptopQA.Mac'
    $targetExecutable = Join-Path $targetApp 'Contents\MacOS\LaptopQA.Mac'
    if ((Get-Item -LiteralPath $sourceExecutable).Length -ne
        (Get-Item -LiteralPath $targetExecutable).Length) {
        throw "The macOS app copy to $driveRoot could not be verified."
    }
    $removableDriveCopies += $targetApp
}

[pscustomobject]@{
    ReleaseFolder = $dist
    App = $appRoot
    Arm64 = Test-Path (Join-Path $appRoot 'Contents\MacOS\LaptopQA.Mac')
    RemovableDriveCopies = $removableDriveCopies
} | ConvertTo-Json
