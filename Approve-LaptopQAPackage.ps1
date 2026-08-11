[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TestEvidence,

    [string]$AcceptedBy = $env:USERNAME
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifestPath = Join-Path $package 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Package manifest not found: $manifestPath"
}

$originalManifestJson = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $originalManifestJson | ConvertFrom-Json
$failures = @()
foreach ($entry in $manifest.Files) {
    $file = Join-Path $package ([string]$entry.Path)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        $failures += "Missing: $($entry.Path)"
        continue
    }
    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($actualHash -ne [string]$entry.SHA256) {
        $failures += "Hash mismatch: $($entry.Path)"
    }
}
if ($failures.Count -gt 0) {
    throw "Package verification failed:`n$($failures -join [Environment]::NewLine)"
}

$sourceCommit = [string]$manifest.SourceCommit
if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
    throw 'The candidate has no source commit. Rebuild it from a committed canonical-source baseline.'
}
$repoRoot = $PSScriptRoot
$detectedRepoRoot = [string](& git -C $repoRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($detectedRepoRoot)) {
    throw 'The approval script is not running from a Git repository.'
}
$repoRoot = (Resolve-Path -LiteralPath $detectedRepoRoot.Trim()).Path
& git -C $repoRoot cat-file -e "$sourceCommit^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "The package source commit is not present in the canonical repository: $sourceCommit"
}
$tagName = "accepted-$($manifest.PackageName)"
& git -C $repoRoot show-ref --verify --quiet "refs/tags/$tagName"
if ($LASTEXITCODE -eq 0) {
    throw "Accepted-package tag already exists: $tagName"
}

if ($PSCmdlet.ShouldProcess($package, 'Mark package manifest Accepted')) {
    $manifest.Status = 'Accepted'
    $manifest | Add-Member -NotePropertyName AcceptedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force
    $manifest | Add-Member -NotePropertyName AcceptedBy -NotePropertyValue $AcceptedBy -Force
    $manifest | Add-Member -NotePropertyName TestEvidence -NotePropertyValue $TestEvidence -Force
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    try {
        & git -C $repoRoot tag -a $tagName $sourceCommit -m "Accepted package $($manifest.PackageName); evidence: $TestEvidence"
        if ($LASTEXITCODE -ne 0) {
            throw "git tag failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        $originalManifestJson | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        throw
    }
}

[pscustomobject]@{
    Package = $package
    Status = if ($WhatIfPreference) { 'Candidate (WhatIf)' } else { 'Accepted' }
    VerifiedFiles = @($manifest.Files).Count
    TestEvidence = $TestEvidence
    SourceTag = $tagName
} | ConvertTo-Json
