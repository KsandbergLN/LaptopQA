# Laptop QA — macOS companion

This is the Avalonia/C# macOS companion for Laptop QA. It lets technicians continue the Windows QA workflow on a personal MacBook. WPF is Windows-only, so Avalonia is used to preserve the glass-card layout, themes, colors, and desktop behavior.

## Workflow consistency

Complete Windows steps 1–7 first; technicians can then use the companion starting with step 8 (QA Output) on their personal MacBook to continue the same Windows QA workflow. It mirrors the Windows workflow and presentation so cached results, notes, and QA records remain consistent. It is an alternate workstation, not a separate Mac-specific checklist.

Retained functionality:

- Cached Windows diagnostics, battery, hardware, network, display, keyboard, and USB results
- Cached Final Checks (step 7), reviewed from the Windows session before the Mac handoff
- QA Output (step 8), including printable/zoomable HTML QA sheets and ServiceNow launch
- QA notes, activity, cached hardware snapshots, managed folders, Light/Dark themes, Config, and Factory Settings
- Full configuration model retained for compatibility

Unavailable on the Mac companion:

- Starting a new session or collecting Windows hardware results directly
- BIOS controls, Windows Autopilot hash collection, and direct Windows device operations
- USB port testing and other steps 1–7 actions, which must be completed in the Windows app first

## Launch

On a technician's personal MacBook, double-click `macOS Laptop QA Launcher.app` from the approved package. Keep it beside the `LAPTOP QA` data folder or use the approved removable-drive package so the cached Windows session can be loaded. Do not launch the internal executable inside the app bundle directly.

## Build

Run `Build-MacRelease.ps1` on Windows to cross-publish the Apple Silicon `macOS Laptop QA Launcher.app`. The `.app` is placed directly at the package or removable-drive root beside the `LAPTOP QA` folder; no macOS instruction text, `.command` file, Terminal window, or PowerShell installation is required. The app discovers the adjacent `LAPTOP QA` data folder or the same folder on an attached macOS volume, so a copied app can still use the removable drive's cached Windows QA session. The final organizational release should be signed and notarized with the organization's Apple Developer identity.

The bundle includes `Contents/Resources/app-icon.icns`, referenced by `CFBundleIconFile`, so Finder, the Dock, and the app switcher display the Laptop QA icon.

The Windows and macOS apps share `LAPTOP QA/.runtime/qa-session.json` and `LAPTOP QA/Laptop-QA-Config.json`. Notes, final checks, technician name, theme, and supported configuration values are written back with atomic merged saves, so edits made in either app appear in the other without removing Windows-only hardware, BIOS, diagnostics, or protected configuration fields. The Mac companion reads and updates the shared session; it does not collect or refresh Windows hardware information.

For normal maintenance, build the Windows app from the repository root with `Build-LaptopQAIteration.ps1` and the macOS companion with `macos/Build-MacRelease.ps1`. The macOS build preserves removable-drive configuration and QA data and produces the directly runnable Apple Silicon app bundle.
