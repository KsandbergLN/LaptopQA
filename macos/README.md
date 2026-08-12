# Laptop QA Testing — macOS

This is the Avalonia/C# macOS companion for Laptop QA. WPF is Windows-only, so Avalonia is used to preserve the glass-card layout, themes, colors, and desktop behavior.

## Workflow consistency

A primary benefit of Laptop QA is consistency. Complete Windows steps 1–7 first; technicians can then use the companion starting with step 8 (QA Output) on their personal Mac. It mirrors the Windows workflow and presentation so cached results, notes, and QA records remain consistent, even where macOS hardware actions are disabled.

Retained functionality:

- Dell preboot diagnostics import, parsing, raw-log search, and unanswered-prompt warnings
- Battery and macOS hardware information
- Cached Final Checks (step 7), reviewed from the Windows session before the Mac handoff
- QA Output (step 8), including printable/zoomable HTML QA sheets and ServiceNow launch
- QA notes, activity, hardware snapshots, managed folders, Light/Dark themes, Config, and Factory Settings
- Full configuration model retained for compatibility

Removed by design:

- Steps 1–5
- BIOS controls
- Windows Autopilot hash collection and direct Windows device operations, which have no macOS API equivalent

## Build

Run `Build-MacRelease.ps1` on Windows to cross-publish the Apple Silicon `macOS Laptop QA Launcher.app`. The `.app` is placed directly at the package or removable-drive root beside the `LAPTOP QA` folder; no macOS instruction text, `.command` file, Terminal window, or PowerShell installation is required. The app discovers the adjacent `LAPTOP QA` data folder or the same folder on an attached macOS volume, so a copied app can still use the removable drive's cached Windows QA session. The final organizational release should be signed and notarized with the organization's Apple Developer identity.

The bundle includes `Contents/Resources/app-icon.icns`, referenced by `CFBundleIconFile`, so Finder, the Dock, and the app switcher display the Laptop QA icon.

The Windows and macOS apps share `LAPTOP QA/.runtime/qa-session.json` and `LAPTOP QA/Laptop-QA-Config.json`. Notes, final checks, technician name, theme, and supported configuration values are written back with atomic merged saves, so edits made in either app appear in the other without removing Windows-only hardware, BIOS, diagnostics, or protected configuration fields. macOS still does not collect or refresh hardware information.

For normal maintenance, build the Windows app from the repository root with `Build-LaptopQAIteration.ps1` and the macOS companion with `macos/Build-MacRelease.ps1`. The macOS build preserves removable-drive configuration and QA data and produces the directly runnable Apple Silicon app bundle.
