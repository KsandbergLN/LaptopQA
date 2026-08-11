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

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
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

if ($PSCmdlet.ShouldProcess($package, 'Mark package manifest Accepted')) {
    $manifest.Status = 'Accepted'
    $manifest | Add-Member -NotePropertyName AcceptedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o')) -Force
    $manifest | Add-Member -NotePropertyName AcceptedBy -NotePropertyValue $AcceptedBy -Force
    $manifest | Add-Member -NotePropertyName TestEvidence -NotePropertyValue $TestEvidence -Force
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

[pscustomobject]@{
    Package = $package
    Status = if ($WhatIfPreference) { 'Candidate (WhatIf)' } else { 'Accepted' }
    VerifiedFiles = @($manifest.Files).Count
    TestEvidence = $TestEvidence
} | ConvertTo-Json
