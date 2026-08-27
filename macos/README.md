# Laptop QA — macOS companion

This is the Avalonia/C# macOS companion for Laptop QA. It lets technicians continue the Windows QA workflow on a personal MacBook. WPF is Windows-only, so Avalonia is used to preserve the glass-card layout, themes, colors, and desktop behavior.

Laptop QA prepares, tests, and documents Windows laptops through a guided session. The companion focuses on the cached-session, final-check, QA-sheet, notes, and ServiceNow portions of that workflow; Windows hardware and diagnostics remain owned by the Windows app.

## Workflow consistency

Complete Windows steps 1–7 first; technicians can then use the companion starting with step 8 (Device Condition) on their personal MacBook to continue the same Windows QA workflow. It mirrors the Windows workflow and presentation so cached results, notes, and QA records remain consistent. It is an alternate workstation, not a separate Mac-specific checklist.

Retained functionality:

- Cached Windows diagnostics, battery, hardware, network, display, keyboard, and USB results
- Device Condition (step 8), including Trackpad Working and Checked Physical Condition
- Final Checks (step 9), including shared checkboxes and configured links
- QA Output (step 10), including printable/zoomable HTML QA sheets and ServiceNow launch
- QA notes, activity, cached hardware snapshots, managed folders, Light/Dark themes, Config, and Factory Settings
- Full configuration model retained for compatibility

Unavailable on the Mac companion:

- Starting a new session or collecting Windows hardware results directly
- BIOS controls, Windows Autopilot hash collection/upload, and direct Windows device operations
- USB port testing and other steps 1–7 actions, which must be completed in the Windows app first

## Launch

On a technician's personal MacBook, open `macOS Laptop QA Launcher.app` from the approved package. Keep it beside the `LAPTOP QA` data folder or use the approved removable-drive package so the cached Windows session can be loaded. Do not launch the internal executable inside the app bundle directly.

## Build

Run `Build-MacRelease.ps1` on Windows to cross-publish the Apple Silicon `macOS Laptop QA Launcher.app`. The `.app` is placed directly at the package or removable-drive root beside the `LAPTOP QA` folder; no macOS instruction text, `.command` file, Terminal window, or PowerShell installation is required. The app discovers the adjacent `LAPTOP QA` data folder or the same folder on an attached macOS volume, so a copied app can still use the removable drive's cached Windows QA session. The final organizational release should be signed and notarized with the organization's Apple Developer identity.

The bundle includes `Contents/Resources/app-icon.icns`, referenced by `CFBundleIconFile`, so Finder, the Dock, and the app switcher display the Laptop QA icon.

The Windows and macOS apps share `LAPTOP QA/.runtime/qa-session.json` and `LAPTOP QA/Laptop-QA-Config.json`. Notes, final checks, technician name, theme, and supported configuration values are written back with atomic merged saves, so edits made in either app appear in the other without removing Windows-only hardware, BIOS, diagnostics, or protected configuration fields. The shared configuration also contains the Windows-only Autopilot hash-upload destination and hardware timing/CLI settings; retaining those values on the Mac keeps the package compatible but does not make the Mac collect hardware or run Windows tools. The Mac companion reads and updates the shared session; it does not collect or refresh Windows hardware information.

The **Final Check Links** in Config are shared. On Windows, Check Hash and Group Tag, Remove User from Laptop in Intune, Update Stockrooms, and Upload Hash open their configured Intune or ServiceNow pages; the first three copy the cached service tag when a manual search is required. On macOS, the companion exposes the three cached final-check actions (Check Hash and Group Tag, Remove User, and Update Stockrooms); it does not collect or upload the Windows Autopilot hash. Keep `{SERIAL}` in the Update Stockrooms URL so the current tag can be inserted. The ServiceNow request button similarly copies the request description and opens the configured page. The macOS app does not fill or submit web forms; technicians paste the copied information where needed.

For normal maintenance, build the Windows app from the repository root with `Build-LaptopQAIteration.ps1` and the macOS companion with `macos/Build-MacRelease.ps1`. The macOS build preserves removable-drive configuration and QA data and produces the directly runnable Apple Silicon app bundle.
