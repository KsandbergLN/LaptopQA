# Laptop QA  Developer Handoff

Last reviewed: 2026-08-14

> **Canonical-source marker:** `CANONICAL-SOURCE.md` is authoritative. Historical version numbers do not override it.

## Source of truth

Use this repository root for current development. It contains the newest maintained C# and XAML files and builds as `LaptopQA.Windows`.

Do not edit these as source:

- `bin`, `obj`, and `dist`: generated build/package output.
- Separately stored V2 workspaces: historical PowerShell files, recovery copies, and verification artifacts.

The canonical source and its shared dependencies are maintained in this repository. Generated output and historical alternatives are excluded. Use reviewed branches; do not edit historical folders directly.

## Build and smoke check

Run from the repository root:

```powershell
dotnet build .\LaptopQA.Windows.csproj -c Release
```

For a packaged iteration, run `Build-LaptopQAIteration.ps1 -NoDeploy`. It creates a validated candidate and SHA-256 manifest but never deploys. Record test evidence with `Approve-LaptopQAPackage.ps1`, then use the separate `Deploy-LaptopQAPackage.ps1` with an explicit `-OneDrive` and/or `-RemovableDrives` target. All scripts support `-WhatIf` where state can change. Do not edit packaged copies under `dist`.

The application is Windows-only (`net10.0-windows`, WPF) and several workflows require Dell hardware, administrator rights, removable media, or Windows applications. A successful build does not replace an on-device smoke test.

### Portable package and removable-drive launch

Windows publishes as `LaptopQA.Windows.exe`. For normal technician use, launch the package-root `Windows Laptop QA Launcher.vbs`; it starts `LAPTOP QA\App\Start Laptop QA Local.ps1` silently. The PowerShell launcher stages only the executable/runtime under `%LOCALAPPDATA%\Laptop QA\Windows`, passes the package folder as `--data-root`, and leaves QA data, configuration, reports, logs, hashes, and hardware snapshots on the removable drive.

The launcher uses `LaptopQA.Windows.exe`. When changing package layout or executable naming, test both entry points:

```text
<iteration root>\Windows Laptop QA Launcher.vbs
<iteration root>\LAPTOP QA\App\Start Laptop QA Local.ps1
```

For a removable-drive update, copy the candidate's `LAPTOP QA\App` contents plus the package-root VBS and `Laptop-QA-Drive.json`. Preserve the target drive's `Laptop-QA-Config.json`, `.runtime`, `activity`, `logs`, `QA sheets`, `hardware`, and `hash` folders. Verify the deployed `LaptopQA.Windows.exe` SHA-256 hash against the package before calling the deployment complete.

## Code map

| Area | Primary file | Notes |
| --- | --- | --- |
| Application startup | `App.xaml`, `App.xaml.cs`, `Start-LaptopQA-Local.ps1`, `Start-LaptopQA-Silent.vbs` | WPF entry point, top-level exception handling, and portable-drive staging/launch behavior. |
| Main UI layout | `MainWindow.xaml` | Main shell, test rows, drawers, menus, and styles. |
| Main workflow | `MainWindow.xaml.cs` | Startup, hardware collection, QA actions, USB detection, caching, report output, ServiceNow automation, and external process calls. Foldable `#region` labels divide these responsibilities. |
| ServiceNow launch | `ServiceNowRequestLauncher.cs`, `MainWindow.xaml.cs` | The primary route opens Edge and sends a page autofill script for the configured request type, assignment group, and QA description. A direct open plus copied description is the startup-failure fallback. |
| Final-check actions | `MainWindow.xaml.cs`, `TransientNotificationWindow.cs` | Check Hash and Group Tag, Remove User from Laptop in Intune, and Update Stockrooms open their configured Intune or ServiceNow page in a new Edge tab and copy the service tag to the clipboard. Update Stockrooms uses Windows UI Automation to select ServiceNow's Serial number field, enter the tag, and press Enter; it must gracefully fall back to the copied tag when the page accessibility tree changes. The themed toast confirms launches without blocking the technician. Completion remains a separate manual checkbox. |
| macOS final-check actions | `macos/MainWindow.axaml`, `macos/MainWindow.axaml.cs`, `macos/SettingsWindow.*` | The macOS companion mirrors the three action buttons, aligned manual checkboxes, shared Final Check Links configuration, service-tag clipboard behavior, and centered themed toast. Each action only opens its configured link and copies the relevant information; macOS does not automate browser forms. |
| Configuration | `AppConfig.cs`, `Laptop-QA-Config.json`, `SettingsWindow.cs` | Defaults, persisted settings, and settings UI. The Final Check Links group holds the three external-action URLs; `UpdateStockroomsUrl` must retain `{SERIAL}` for dynamic service-tag replacement. |
| Session persistence | `QaSessionCache.cs`, `QaStepCache.cs`, `UsbPortCache.cs` | The active QA is saved as `.runtime/qa-session.json`; searchable 90-day history is stored as stable session files under `.runtime/sessions`, with an atomic `.runtime/sessions-index.json` metadata index that can be rebuilt from those snapshots. The steps 1-7 handoff prompt is tracked per session, so it appears once only after all test rows are complete. |
| Logging | `ErrorLog.cs` | Activity/error log paths, migration, redaction, and session filenames. |
| Hardware models/UI | `HardwareSnapshot.cs`, `HardwareWindow.cs` | Collected device data and hardware drawer/window. |
| Diagnostics/report UI | `DiagnosticsResult.cs`, `QaSheetFiles.cs`, `QaSheetImageWindow.cs`, `macos/Services/DiagnosticsParser.cs` | Diagnostics results and QA sheet display/output. Unanswered Dell interactive prompts are reported with their detected category (for example Video, Audio, Camera, Keyboard, or pointing device) rather than a generic warning. |
| Keyboard test | `KeyboardTesterWindow.cs` | Standalone keyboard tester window. |
| Localization | `LanguageCatalog.cs`, `WpfLocalization.cs`, `Shared\UiLocalization.cs`, `Shared\ui-translations.json` | Culture selection and translated UI strings. Shared files are linked by the project file. |
| Packaging | `Build-LaptopQAIteration.ps1`, `Deploy-LaptopQAPackage.ps1`, `Start-LaptopQA-Local.ps1`, `Start-LaptopQA-Silent.vbs` | Produces the self-contained V4 package and validates/deploys its portable launcher. |

## Warranty Date Behavior

Warranty lookup remains separate from warranty status display. Keep the stored warranty value as the plain expiration date, such as `2029-06-18`; do not persist the visual status marker with the date.

When rendering the app header or QA sheet, `MainWindow.xaml.cs` adds:

- `✓` when the expiration date is today or later.
- `X` when the expiration date is before today or unavailable.

The comparison date is pulled from network HTTP `Date` headers first, using Microsoft/Bing/Dell endpoints. If network date lookup fails, the app falls back to the local Windows system clock. This matters in OOBE because the Windows clock is available but can be wrong before time sync.

## MainWindow navigation labels

`MainWindow.xaml.cs` is organized into these IDE-foldable regions:

1. Shared types, constants, and runtime state
2. Window lifecycle, configuration, theming, and startup
3. Live device monitoring, storage cleanup, and startup data collection
4. QA test actions and output
5. USB port detection and scoring
6. Completion, power actions, drawers, and managed folders
7. Drawer layout, QA session persistence, settings, and window commands
8. Logging, process execution, reports, and integration helpers

Search for a region name first, then search for the visible button/control name from `MainWindow.xaml`. WPF event handlers normally end in `_Click`, `_Changed`, `_Loaded`, or `_Closing`.

## Review findings and recommended cleanup order

1. Split `MainWindow.xaml.cs` into partial class files by the regions above. It is roughly 300 KB and is the largest maintenance risk. Keep the first split mechanical: move methods without changing behavior.
2. Add unit-testable services for process execution, data-root selection, diagnostics parsing, and filename cleanup. These contain useful logic but are currently coupled to the window.
3. Keep the compiler baseline at zero. Nullable warnings were corrected on 2026-08-11 and warnings are treated as errors.
4. Replace repeated raw string state values (`Waiting`, `Working`, `Ok`, `Bad`, `Warning`, `Ignored`) with an enum or a single constants type. Update persistence compatibility deliberately.
5. Centralize defaults currently repeated between `AppConfig` and `MainWindow` (especially ServiceNow settings and retention periods).
6. Move long embedded PowerShell scripts out of C# string constants into versioned script resources. This will make both languages testable and easier to review.
7. Add automated tests before changing diagnostics parsing, USB scoring, QA completion, or cache migration. Those paths affect pass/fail results and should not be refactored from build verification alone.

## Changes from the 2026-08-10 cleanup

- Added region labels to `MainWindow.xaml.cs` for fast navigation.
- Changed the completion animation helper from non-event `async void` to a fire-and-forget `Task` whose body already catches and logs failures.
- Removed two no-op decompiler artifacts (`_ = 2;`).
- Made `SafeFile` return its fallback when sanitizing leaves an empty filename.
- Marked intentional background refresh calls explicitly with `_ =`.

## Minimum manual smoke test after UI/workflow changes

- Launch as administrator and confirm startup/splash completes.
- Confirm the selected data root and log locations.
- Open Settings and verify theme/language changes.
- Exercise Wi-Fi/Ethernet, camera, keyboard, external display, and USB rows on suitable hardware.
- Load or browse to a Dell diagnostics log and verify its result.
- Verify an unanswered Dell diagnostics prompt names the affected prompt category in both the main UI and QA sheet.
- Complete Windows steps 1-7 on a device with BIOS USB connector data; confirm the handoff prompt appears only after the USB port count is detected and every detected port has a result.
- Save/open a QA sheet and confirm the output image.
- Select ServiceNow and verify the request type, assignment group, and description are populated; confirm the QA description is returned to the clipboard afterward.
- Launch the packaged app from the root VBS and directly from the app-folder PowerShell script; verify both retain the removable package as the data root.
- When deploying to a removable drive, verify the executable hash and confirm that the existing configuration and QA data folders were preserved.
- Reset the QA session, close/reopen the app, and confirm cache behavior.
- Test shutdown/reboot/BIOS actions only on a disposable QA device.
