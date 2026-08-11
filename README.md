# Laptop QA

Laptop QA contains two supported technician applications:

- **Windows app** — WPF on .NET 10, built from the repository root.
- **macOS companion** — Avalonia on .NET 10, built from `macos\`.

The repository root is the canonical Windows source. The `main` branch is authoritative. Generated build output, runtime data, logs, and release packages are intentionally ignored.

## Start here

- [`CANONICAL-SOURCE.md`](CANONICAL-SOURCE.md) — source-of-truth rules and generated-file boundaries.
- [`DEVELOPER-HANDOFF.md`](DEVELOPER-HANDOFF.md) — architecture, testing, release, recovery, and ownership-transfer guidance.
- [`macos/README.md`](macos/README.md) — macOS workflow, shared data behavior, and Apple Silicon packaging.

## Prerequisites

- Windows 10/11 development environment.
- PowerShell and .NET SDK 10.0.x.
- A supported test device for hardware, diagnostics, USB, display, battery, and administrator-only workflows.
- For organizational macOS releases, an Apple Silicon validation device and the organization’s signing/notarization process.

## Build

Run each project independently from the repository root:

```powershell
dotnet build .\LaptopQATestingV4.csproj -c Release --no-incremental
dotnet build .\macos\LaptopQATestingMac.csproj -c Release --no-incremental
```

The Windows project explicitly excludes `macos\**\*.cs`; do not remove that boundary or add macOS files to the Windows project. The current Windows validation target is 0 warnings and 0 errors. The macOS project currently has 24 Avalonia/Skia deprecation warnings and 0 errors.

## Where to make changes

- `MainWindow.xaml` and `MainWindow.xaml.cs` — Windows shell and core workflow.
- `AppConfig.cs`, `Laptop-QA-Config.json`, and `SettingsWindow.cs` — configuration and settings.
- `Shared\` — localization and shared resources.
- `macos\` — Avalonia companion source, services, assets, and build script.
- `tools\` — packaged hardware and support utilities.

Do not edit generated `bin\`, `obj\`, or `dist\` contents as source. Make changes on a reviewed branch based on `main`.

## Release flow

Use the scripts as separate stages:

1. `Build-LaptopQAIteration.ps1` creates a validated **Candidate** package and records its source commit and file hashes.
2. `Audit-LaptopQAPackages.ps1` checks package completeness and can quarantine incomplete output.
3. `Approve-LaptopQAPackage.ps1` verifies hashes, records test evidence, marks the manifest **Accepted**, and creates the acceptance tag.
4. `Deploy-LaptopQAPackage.ps1` deploys only an accepted package to an explicitly selected approved target.

Use `-WhatIf` for state-changing rehearsals. Never edit a package after approval; rebuild it from the committed source if anything changes.

## Validation expectations

A successful compile is necessary but not sufficient. After workflow or UI changes, run the acceptance checklist in `DEVELOPER-HANDOFF.md` on approved hardware, verify the packaged application from its package location, and preserve the source commit, package manifest, test evidence, and acceptance tag.
