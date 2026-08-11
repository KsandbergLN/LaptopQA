[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'LaptopQA.Windows.csproj'
$dist = Join-Path $projectDir 'dist'
$packageName = "LaptopQATestingV4-Iteration-$Stamp"
$stagingRoot = Join-Path $dist '.staging'
$quarantineRoot = Join-Path $dist 'quarantine'
$working = Join-Path $stagingRoot $packageName
$final = Join-Path $dist $packageName
$packageFolder = Join-Path $working 'LAPTOP QA'
$appFolder = Join-Path $packageFolder 'App'
$handoffRoot = $projectDir
$startScriptSource = Join-Path $handoffRoot 'Start-LaptopQA-Local.ps1'
$startScriptTarget = Join-Path $appFolder 'Start Laptop QA Local.ps1'
$silentLauncherSource = Join-Path $handoffRoot 'Start-LaptopQA-Silent.vbs'
$silentLauncherTarget = Join-Path $working 'Windows Laptop QA Launcher.vbs'
$driveMarkerSource = Join-Path $handoffRoot 'Laptop-QA-Drive.json'
$driveMarkerTarget = Join-Path $working 'Laptop-QA-Drive.json'

if ($WhatIfPreference) {
    [pscustomobject]@{
        Action = 'Build package only'
        StagingFolder = $working
        FinalFolder = $final
        Deployment = if ($NoDeploy) { 'Disabled explicitly with -NoDeploy' } else { 'Disabled by default; use Deploy-LaptopQAPackage.ps1 after approval' }
    } | ConvertTo-Json
    return
}

if (Test-Path -LiteralPath $working -PathType Container) {
    throw "Staging folder already exists: $working"
}
if (Test-Path -LiteralPath $final -PathType Container) {
    throw "Final package folder already exists: $final"
}

New-Item -ItemType Directory -Force -Path $appFolder, $quarantineRoot | Out-Null

try {
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -p:EnableCompressionInSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=false `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $appFolder
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Force -Path `
        (Join-Path $packageFolder 'hash'), `
        (Join-Path $packageFolder 'QA sheets'), `
        (Join-Path $packageFolder 'hardware'), `
        (Join-Path $packageFolder 'logs'), `
        (Join-Path $packageFolder 'activity'), `
        (Join-Path $packageFolder '.runtime') | Out-Null

    $publishedConfig = Join-Path $appFolder 'Laptop-QA-Config.json'
    $packageConfig = Join-Path $packageFolder 'Laptop-QA-Config.json'
    if (Test-Path -LiteralPath $publishedConfig -PathType Leaf) {
        Copy-Item -LiteralPath $publishedConfig -Destination $packageConfig -Force
        Remove-Item -LiteralPath $publishedConfig -Force
    }

    Get-ChildItem -LiteralPath $appFolder -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    if (Test-Path -LiteralPath $startScriptSource -PathType Leaf) {
        Copy-Item -LiteralPath $startScriptSource -Destination $startScriptTarget -Force
    }
    if (Test-Path -LiteralPath $silentLauncherSource -PathType Leaf) {
        Copy-Item -LiteralPath $silentLauncherSource -Destination $silentLauncherTarget -Force
    }
    if (Test-Path -LiteralPath $driveMarkerSource -PathType Leaf) {
        Copy-Item -LiteralPath $driveMarkerSource -Destination $driveMarkerTarget -Force
    }

    $requiredFiles = @(
        (Join-Path $appFolder 'LaptopQA.Windows.exe'),
        $packageConfig,
        $startScriptTarget,
        $silentLauncherTarget,
        $driveMarkerTarget
    )
    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "Package validation failed. Missing: $($missingFiles -join '; ')"
    }

    $sourceCommit = $null
    try {
        $commitOutput = & git -C $projectDir rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $sourceCommit = [string]$commitOutput
        }
    }
    catch {
        $sourceCommit = $null
    }

    $files = @(Get-ChildItem -LiteralPath $working -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName.Substring($working.Length + 1)
                Length = $_.Length
                SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })

    $manifest = [ordered]@{
        SchemaVersion = 1
        PackageName = $packageName
        Product = 'LaptopQA.Windows'
        Runtime = 'win-x64'
        CreatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        SourceCommit = $sourceCommit
        Status = 'Candidate'
        FileCount = $files.Count
        TotalBytes = ($files | Measure-Object Length -Sum).Sum
        Files = $files
    }
    $manifestPath = Join-Path $working 'package-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

    Move-Item -LiteralPath $working -Destination $final

    [pscustomobject]@{
        IterationFolder = $final
        Manifest = Join-Path $final 'package-manifest.json'
        Status = 'Candidate'
        Deployment = if ($NoDeploy) { 'Disabled explicitly with -NoDeploy' } else { 'Not performed (build-only workflow)' }
        ExeExists = $true
        FolderSizeMB = [math]::Round((Get-ChildItem -LiteralPath $final -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
        FileCount = (Get-ChildItem -LiteralPath $final -Recurse -File | Measure-Object).Count
    } | ConvertTo-Json
}
catch {
    if (Test-Path -LiteralPath $working -PathType Container) {
        $quarantineTarget = Join-Path $quarantineRoot "$packageName-failed-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Move-Item -LiteralPath $working -Destination $quarantineTarget
        Write-Warning "Incomplete package moved to quarantine: $quarantineTarget"
    }
    throw
}
