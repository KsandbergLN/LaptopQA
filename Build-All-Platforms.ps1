[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'

throw @'
Build-All-Platforms.ps1 is disabled because it combined canonical V4 packaging with archived V5/macOS builds and direct removable-drive deployment.

Use the governed Windows workflow instead:
  .\V4\Build-LaptopQAIteration.ps1 -Stamp <stamp> -NoDeploy
  .\V4\Approve-LaptopQAPackage.ps1 -PackagePath <candidate> -TestEvidence <evidence>
  .\V4\Deploy-LaptopQAPackage.ps1 -PackagePath <accepted-package> -OneDrive|-RemovableDrives -WhatIf

Create a separately reviewed cross-platform workflow if macOS packaging becomes supported again.
'@
