# Laptop QA — macOS companion

This is the Avalonia/C# macOS companion for Laptop QA. It lets technicians continue the Windows QA workflow on a personal MacBook. WPF is Windows-only, so Avalonia is used to preserve the glass-card layout, themes, colors, and desktop behavior. The current companion baseline is tagged `laptopqa-macos-2026.09.15`.

Laptop QA prepares, tests, and documents Windows laptops through a guided session. The companion focuses on the cached-session, diagnostics review, final-check, QA-sheet, notes, and Create Ticket portions of that workflow; Windows hardware collection and Windows-only diagnostics execution remain owned by the Windows app.

## Workflow consistency

Complete Windows steps 1–7 first; technicians can then use the companion starting with step 8 (Device Condition) on their personal MacBook to continue the same Windows QA workflow. It mirrors the Windows workflow and presentation so cached results, notes, and QA records remain consistent. It is an alternate workstation, not a separate Mac-specific checklist.

Retained functionality:

- Cached Windows diagnostics, battery, hardware, network, display, keyboard, and USB results
- Device Condition (step 8), including Trackpad Working and Checked Physical Condition
- Final Checks (step 9), including shared checkboxes and configured links
- QA Output (step 10), including printable/zoomable HTML QA sheets, a final battery-health result, and Create Ticket launch
- QA notes, activity, cached hardware snapshots, managed folders, Light/Dark themes, Config, and Factory Settings
- Full configuration model retained for compatibility

Unavailable on the Mac companion:

- Starting a new session or collecting Windows hardware results directly
- BIOS controls, Windows Autopilot hash collection/upload, and direct Windows device operations
- USB port testing and other steps 1–7 actions, which must be completed in the Windows app first

The companion does not collect Mac hardware facts or add a Mac-specific pass/fail checklist. Its battery header and QA sheet use the cached Windows capacity percentage plus the Dell diagnostics health rating. Excellent and Good are passing ratings; Fair and Poor are failing ratings; a missing rating remains unavailable.

## Launch

On a technician's personal MacBook, open `macOS Laptop QA Launcher.app` from the approved package. Keep it beside the `LAPTOP QA` data folder or use the approved removable-drive package so the cached Windows session can be loaded. Do not launch the internal executable inside the app bundle directly.

## Build

Run `Build-MacRelease.ps1` on Windows to cross-publish the Apple Silicon `macOS Laptop QA Launcher.app`. The `.app` is placed directly at the package or removable-drive root beside the `LAPTOP QA` folder; no macOS instruction text, `.command` file, Terminal window, or PowerShell installation is required. The app discovers the adjacent `LAPTOP QA` data folder or the same folder on an attached macOS volume, so a copied app can still use the removable drive's cached Windows QA session. The final organizational release should be signed and notarized with the organization's Apple Developer identity.

The bundle includes `Contents/Resources/app-icon.icns`, referenced by `CFBundleIconFile`, so Finder, the Dock, and the app switcher display the Laptop QA icon.

The Windows and macOS apps share `LAPTOP QA/.runtime/qa-session.json` and `LAPTOP QA/Laptop-QA-Config.json`. Notes, final checks, technician name, theme, and supported configuration values are written back with atomic merged saves, so edits made in either app appear in the other without removing Windows-only hardware, BIOS, diagnostics, or protected configuration fields. The companion normalizes older packages whose Check Hash and Remove User links were stored in opposite slots and removes retired credential-era keys when it saves shared configuration. The Mac companion reads and updates the shared session; it does not collect or refresh Windows hardware information.

The **Final Check Links** in Config are shared. On macOS, Check Hash and Group Tag, Remove User from Laptop in Intune, and Update Stockrooms open their configured Intune or ServiceNow page and copy the cached service tag. `UploadHashUrl` is retained for Windows compatibility but hash collection/upload remains Windows-only. Keep `{SERIAL}` in the Update Stockrooms URL so the current tag can be inserted. The **Create Ticket** button copies the request description and opens the configured page. The macOS app does not fill or submit web forms; technicians paste the copied information where needed.

When a cached session has no diagnostics log, startup checks mounted FAT32 diagnostics media. It prefers a fresh `DellPrebootDiagnosticsLog.txt`, then a single unambiguous retained log or a filename containing the active service tag; it does not guess among multiple unmatched files. **Browse** can select a log manually and **Raw Log** shows the retained text. An existing cached diagnostics result is preserved when no new unambiguous log is available.

For normal maintenance, build the Windows app from the repository root with `Build-LaptopQAIteration.ps1` and the macOS companion with `macos/Build-MacRelease.ps1`. The macOS build preserves removable-drive configuration and QA data and produces the directly runnable Apple Silicon app bundle.
