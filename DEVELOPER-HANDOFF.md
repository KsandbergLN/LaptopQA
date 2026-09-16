# Laptop QA  Developer Handoff

Last reviewed: 2026-09-16

> **Canonical-source marker:** `CANONICAL-SOURCE.md` is authoritative. Historical version numbers do not override it.

## Source of truth

Use this repository root for current development. It contains the newest maintained C# and XAML files and builds as `LaptopQA.Windows`.

Do not edit these as source:

- `bin`, `obj`, and `dist`: generated build/package output.
- Separately stored V2 workspaces: historical PowerShell files, recovery copies, and verification artifacts.

The canonical source and its shared dependencies are maintained in this repository. Generated output and historical alternatives are excluded. Use reviewed branches; do not edit historical folders directly.

Laptop QA is the technician workflow for preparing, testing, and documenting Windows laptops. The Windows app owns hardware, diagnostics, BIOS, USB, battery-health, device-condition, hash upload, and QA-output operations; the macOS companion resumes the cached Windows session from step 8 for device condition, final checks, QA-sheet generation, diagnostics review, and Create Ticket preparation.

The current accepted release baseline is tagged `laptopqa-windows-2026.09.15` and `laptopqa-macos-2026.09.15`. Treat the source commit, package manifest, test evidence, and acceptance tag as one release record; do not identify a package by a copied executable alone.

## Build and smoke check

Run from the repository root:

```powershell
dotnet build .\LaptopQA.Windows.csproj -c Release
dotnet build .\macos\LaptopQA.Mac.csproj -c Release
```

For a packaged iteration, run `Build-LaptopQAIteration.ps1 -NoDeploy`. It creates a validated candidate and SHA-256 manifest but never deploys. Record test evidence with `Approve-LaptopQAPackage.ps1`, then use the separate `Deploy-LaptopQAPackage.ps1` with an explicit `-OneDrive` and/or `-RemovableDrives` target. All scripts support `-WhatIf` where state can change. Do not edit packaged copies under `dist`.

The Windows application is `net10.0-windows` WPF and the macOS companion is Avalonia on .NET 10. Several Windows workflows require Dell hardware, administrator rights, removable media, Edge, or Windows applications. A successful build does not replace an on-device smoke test. The Windows validation target is 0 warnings and 0 errors; review the macOS build output for Avalonia/Skia deprecations before accepting a companion release.

### Portable package and removable-drive launch

Windows publishes as `LaptopQATestingV4.exe`. For normal technician use, launch the package-root `Windows Laptop QA Launcher.vbs`; it starts `LAPTOP QA\App\Start Laptop QA Local.ps1` silently. The PowerShell launcher stages only the executable/runtime under `%LOCALAPPDATA%\Laptop QA\Windows`, passes the package folder as `--data-root`, and leaves QA data, configuration, reports, logs, hashes, and hardware snapshots on the removable drive.

The launcher uses `LaptopQATestingV4.exe`. When changing package layout or executable naming, test both entry points:

```text
<iteration root>\Windows Laptop QA Launcher.vbs
<iteration root>\LAPTOP QA\App\Start Laptop QA Local.ps1
```

For a removable-drive update, copy the candidate's `LAPTOP QA\App` contents plus the package-root VBS and `Laptop-QA-Drive.json`. Preserve the target drive's `Laptop-QA-Config.json`, `.runtime`, `activity`, `logs`, `QA sheets`, `hardware`, and `hash` folders. Verify the deployed `LaptopQATestingV4.exe` SHA-256 hash against the package before calling the deployment complete.

## Configuration reference

`Laptop-QA-Config.json` is read from the selected data root. Settings saves merge known values into the existing JSON and write atomically, so a newer build does not discard unknown or Windows-only fields. Missing or unreadable configuration falls back to the defaults in `AppConfig.cs`. The macOS companion reads and updates the same file for supported values; retaining a Windows-only value on the Mac does not enable Windows hardware or command-line behavior.

Do not store passwords, access tokens, or other secrets in this file. URLs and ServiceNow identifiers are routing/configuration values and should be changed only to approved organization destinations.

| Setting | Purpose and behavior |
| --- | --- |
| `TechnicianName` | Saved technician name shown in the header, session, and QA sheet; requested on first run when blank. |
| `AppTheme` | Appearance palette: `Light`, `Dark`, or `AMOLED`. |
| `AppLanguage` | UI and report culture, default `en-US`. |
| `CameraRoll` | Camera Roll folder used by the camera test and cleanup/open-folder actions; environment variables are expanded. |
| `DellDiagnosticsLogFolder` | Optional folder to search for Dell preboot diagnostics logs and retained source archives. |
| `CameraRollCleanupTimeoutSeconds` | Maximum time to wait for camera files to clear, default `30`. |
| `CameraRollCleanupRetryDelaySeconds` | Delay between camera cleanup attempts, default `2`. |
| `WifiRescanEthernetDisableDelaySeconds` | Wait after temporarily disabling active Ethernet before a Wi-Fi rescan, default `3`. |
| `EthernetRestoreDelaySeconds` | Wait after restoring Ethernet following a Wi-Fi rescan, default `2`. |
| `DellWarrantyCliPath` | Optional explicit Dell Warranty CLI path; blank uses installed-tool discovery/fallback. |
| `AutopilotGroupTag` | Expected Windows Autopilot group tag used when validating the exported hash CSV, default `LNG AAD`. |
| `QaComputerNameFormat` | Device-name pattern used in the app, QA sheets, and saved files. Tokens: `{serial}`, `{computer}`, and `{asset}`; default `LNG-{serial}`. |
| `ServiceNowRequestUrl` | Generic Request page opened by Create Ticket/ServiceNow. |
| `ServiceNowTypeOfRequest` | Request type that best-effort browser automation attempts to select, default `Other`. |
| `ServiceNowAssignmentGroupName` | Display name used by ServiceNow browser autofill. |
| `ServiceNowAssignmentGroupSysId` | ServiceNow assignment-group `sys_id` used by browser autofill. |
| `ServiceNowAutomationDelayMilliseconds` | Edge/page readiness wait; clamped to `500`–`30000` ms, default `500`. |
| `CheckHashAndGroupTagUrl` | Intune page opened for manual hash and group-tag verification. |
| `RemoveUserFromIntuneUrl` | Intune page opened for manual user removal. |
| `UpdateStockroomsUrl` | ServiceNow hardware-list page; retain `{SERIAL}` so the active service tag can be inserted. |
| `UploadHashUrl` | Intune Autopilot Devices page opened for manual hash upload after **Export Hash**. |

The Settings dialog validates timing fields as whole numbers and clamps the ServiceNow delay. **Reset Settings** restores defaults and removes the saved technician name; it does not delete QA sheets, logs, hardware files, or session history. Both apps normalize older packages that stored the Check Hash and Remove User links in opposite slots. The macOS save path also removes retired credential-era keys; no credentials are supported in configuration.

## Code map

| Area | Primary file | Notes |
| --- | --- | --- |
| Application startup | `App.xaml`, `App.xaml.cs`, `Start-LaptopQA-Local.ps1`, `Start-LaptopQA-Silent.vbs` | WPF entry point, top-level exception handling, and portable-drive staging/launch behavior. |
| Main UI layout | `MainWindow.xaml` | Main shell, test rows, drawers, menus, styles, fixed 1280x720 design surface, and proportional `Viewbox` scaling. |
| Borderless window behavior | `BorderlessWindowResizer.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs` | Adds `WS_THICKFRAME`, DPI-aware 14-DIP native hit testing, `WM_SIZING` aspect-ratio enforcement, and lifecycle-safe hook cleanup. The restored window keeps the 16:9 design ratio; maximized mode uses the normal Windows work area with square shell corners and no ratio constraint. |
| Main workflow | `MainWindow.xaml.cs` | Startup, hardware collection, battery polling, QA actions, USB detection, caching, report output, Create Ticket/ServiceNow automation, and external process calls. The QA sheet ends with battery health: Excellent or Good is Pass, lower ratings are Fail, and the detail records the Dell rating plus Windows capacity percentage. Foldable `#region` labels divide these responsibilities. |
| ServiceNow launch | `ServiceNowRequestLauncher.cs`, `MainWindow.xaml.cs` | The primary route opens Edge and sends a page autofill script for the configured request type, assignment group, and QA description. A direct open plus copied description is the startup-failure fallback. |
| Final-check actions | `MainWindow.xaml.cs`, `TransientNotificationWindow.cs` | Check Hash and Group Tag, Remove User from Laptop in Intune, Update Stockrooms, and Upload Hash open their configured Intune or ServiceNow page in a new Edge tab and copy the service tag when a manual search is required. Update Stockrooms uses Windows UI Automation to select ServiceNow's Serial number field, enter the tag, and press Enter; it must gracefully fall back to the copied tag when the page accessibility tree changes. The themed toast confirms launches without blocking the technician. Completion remains a separate manual checkbox. |
| macOS final-check actions | `macos/MainWindow.axaml`, `macos/MainWindow.axaml.cs`, `macos/SettingsWindow.*` | The macOS companion mirrors the three cached final-check actions (Check Hash and Group Tag, Remove User, and Update Stockrooms), aligned manual checkboxes, shared link configuration, service-tag clipboard behavior, and Create Ticket details. It does not collect or upload the Windows Autopilot hash and does not automate browser forms. |
| Configuration | `AppConfig.cs`, `Laptop-QA-Config.json`, `SettingsWindow.cs`, `macos/Services/ConfigService.cs` | Defaults, persisted settings, shared-data-root behavior, legacy link normalization, and settings UI. The Final Check Links group holds four URLs, including Windows-only `UploadHashUrl`; `UpdateStockroomsUrl` must retain `{SERIAL}` for dynamic service-tag replacement. |
| Session persistence | `QaSessionCache.cs`, `QaStepCache.cs`, `UsbPortCache.cs` | The active QA is saved as `.runtime/qa-session.json`; searchable 90-day history is stored as stable session files under `.runtime/sessions`, with an atomic `.runtime/sessions-index.json` metadata index that can be rebuilt from those snapshots. The steps 1-7 handoff prompt is tracked per session, so it appears once only after all test rows are complete. |
| USB retest | `MainWindow.xaml`, `MainWindow.xaml.cs` | The **Retest** button clears every USB port result, not only failures, preserves the detected port count, and restarts live monitoring. A technician must move the readable test drive through every port again after selecting it. |
| Logging | `ErrorLog.cs` | Activity/error log paths, migration, redaction, and session filenames. |
| Hardware models/UI | `HardwareSnapshot.cs`, `HardwareWindow.cs` | Collected device data and hardware drawer/window. |
| Diagnostics/report UI | `DiagnosticsResult.cs`, `QaSheetFiles.cs`, `QaSheetImageWindow.cs`, `macos/MainWindow.axaml.cs`, `macos/Services/DiagnosticsParser.cs` | Diagnostics results and QA sheet display/output. Windows and macOS auto-discovery prefer a fresh `DellPrebootDiagnosticsLog.txt`, then accept a single retained `DellPrebootDiagnosticsLog*.txt` file or one whose filename matches the active service tag; neither silently chooses between multiple unmatched retained logs. Startup checks connected Dell diagnostics media when a restored QA session has no diagnostics log, but preserves an existing cached result. Windows retains the original source log with service-tag/timestamp naming and cleans old retained archives. macOS can load an unambiguous missing log from mounted FAT32 diagnostics media. Unanswered Dell interactive prompts are reported with their detected category (for example Video, Audio, Camera, Keyboard, or pointing device) rather than a generic warning. |
| Keyboard test | `KeyboardTesterWindow.cs` | Standalone keyboard tester window. |
| Localization | `LanguageCatalog.cs`, `WpfLocalization.cs`, `Shared\UiLocalization.cs`, `Shared\ui-translations.json` | Culture selection and translated UI strings. Shared files are linked by the project file. |
| Packaging | `Build-LaptopQAIteration.ps1`, `Deploy-LaptopQAPackage.ps1`, `Start-LaptopQA-Local.ps1`, `Start-LaptopQA-Silent.vbs` | Produces the self-contained V4 package and validates/deploys its portable launcher. |

## Behavioral invariants

- Battery health has two sources: the Dell diagnostics log supplies the health rating (`Excellent`, `Good`, `Fair`, or `Poor`), while Windows supplies the capacity percentage and the live current-battery status. The QA result passes only `Excellent` or `Good`; missing diagnostics health is shown as unavailable rather than inferred from charge level.
- Diagnostics selection is conservative. Prefer a fresh canonical log, then one unambiguous retained log or a service-tag match. Never silently choose among multiple unmatched retained logs. A cached diagnostics result is preserved when startup cannot find a newer source.
- USB **Retest** clears every port result, preserves the detected topology, and requires a readable test drive to be moved through every detected port again before the handoff prompt can appear.
- The restored Windows window uses a 16:9 design surface and proportional resize behavior. Maximized mode uses the Windows work area and square shell corners; do not change the native resize hook without testing DPI, edge/corner hit testing, maximize/restore, and cleanup.
- Create Ticket/ServiceNow is best effort. The app opens Edge and attempts field autofill, but always returns the request description to the clipboard. Browser timing or accessibility changes must leave a manual copy/paste path.
- The macOS companion is a second workstation for the Windows workflow. It reads the cached session and shared configuration, can review/repair diagnostics input from mounted FAT32 media, and can continue steps 8–10; it must not collect Windows hardware, run BIOS/USB/hash-upload operations, or introduce a separate Mac checklist.

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

## Recent accepted changes

- 2026-09-15: added the Dell battery-health rating to the QA sheet and aligned the macOS companion with the cached rating and Windows capacity percentage.
- 2026-09-15: macOS can recover a missing diagnostics log from an unambiguous mounted FAT32 diagnostics volume when the cached session has no log.
- 2026-09-10 and 2026-09-04: startup and retained-log discovery now distinguish fresh, service-tag-matched, and ambiguous diagnostics files without silently choosing the wrong result.
- 2026-09-03: added responsive borderless-window resizing with aspect-ratio and maximize/restore behavior.
- 2026-09-01: added full USB retest behavior; Retest clears all port results and requires a new pass through every detected port.
- 2026-08-27: completed the current Windows/macOS workflow and configuration documentation, including Create Ticket fallback, shared session behavior, and final-check link guidance.

## Minimum manual smoke test after UI/workflow changes

- Launch as administrator and confirm startup/splash completes.
- Confirm the selected data root and log locations.
- Open Settings and verify theme/language changes, device-name tokens, Autopilot group tag, folder paths, final-check links, and reset behavior. Confirm `{SERIAL}` remains in the Update Stockrooms URL and that reset leaves QA outputs intact.
- Exercise Wi-Fi/Ethernet, camera, keyboard, external display, and USB rows on suitable hardware.
- Load a fresh Dell diagnostics log, then test one retained service-tag-matched log and an ambiguous set; verify the app refuses to guess among unmatched files.
- Remove the cached diagnostics log and verify Windows checks connected FAT32 diagnostics media at startup; repeat with the macOS companion and verify it can load an unambiguous mounted log without replacing an existing cached result.
- Verify the diagnostics source log is retained with service-tag/timestamp naming and that old retained archives follow the 90-day cleanup policy.
- Verify battery health from a diagnostics log and confirm the QA sheet records rating plus Windows capacity percentage; test Excellent/Good pass and Fair/Poor fail behavior.
- Verify an unanswered Dell diagnostics prompt names the affected prompt category in both the main UI and QA sheet.
- Complete Windows steps 1-7 on a device with BIOS USB connector data; confirm the handoff prompt appears only after the USB port count is detected and every detected port has a result. Select USB **Retest**, verify every port returns to an untested state, then repeat the full port pass. Verify Device Condition, Final Checks, hash upload, and QA Output behavior.
- Resize the restored Windows app from every edge and corner at the supported display scaling values; confirm the UI remains proportionate, the cursor matches the edge, and the opposite edge remains anchored. Maximize and restore it; confirm the shell becomes square only while maximized and the maximize glyph/tool tip remains accurate.
- Save/open a QA sheet and confirm the output image.
- Select Create Ticket and verify the request type, assignment group, and description are populated; confirm the QA description is returned to the clipboard afterward.
- Break or delay Edge automation and confirm the direct-open/clipboard fallback remains usable without automatic submission.
- Launch the packaged app from the root VBS and directly from the app-folder PowerShell script; verify both retain the removable package as the data root.
- When deploying to a removable drive, verify the executable hash and confirm that the existing configuration and QA data folders were preserved.
- Reset the QA session, close/reopen the app, and confirm cache behavior.
- Open the cached session with the macOS companion and confirm shared technician name, theme/language, notes, final-check links, battery result, and session data remain intact; confirm Windows-only hash upload, BIOS, USB, and steps 1–7 remain unavailable there.
- Test shutdown/reboot/BIOS actions only on a disposable QA device.
