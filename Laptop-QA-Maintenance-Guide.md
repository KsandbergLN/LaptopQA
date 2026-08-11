# Laptop QA Testing - Maintenance Guide

> **Superseded source pointer (2026-08-11):** The canonical maintained Windows source is `D:\LaptopQA\V4`, as declared by `SOURCE-OF-TRUTH.md` and `V4\CANONICAL-SOURCE.md`. The older V5 statements below are retained only as history and must not be used to choose editable source or a release.

Last updated: 2026-07-14

This document is a handoff guide for maintaining the Laptop QA Testing app if the original chat history is unavailable.

## Purpose and Operational Benefit

Laptop QA Testing is intended to improve both the quality and consistency of laptop preparation. It gives technicians the same ordered workflow, prompts, terminology, final checks, and QA output for each device. This reduces variation between technicians and shifts, makes skipped or unanswered checks easier to identify, and produces more consistent records for follow-up and auditing.

Workflow consistency is a functional requirement of the app. When adding or changing a feature, preserve the common test sequence and shared output format unless there is a clear operational reason to make them different.

## Current App Versions

- `D:\LaptopQA\V5` is the current test/development version.
- `D:\LaptopQA\V4` is the prior stable comparison version.
- `C:\V2` is reserved for the original V2 PowerShell/WPF version and shared historical notes.
- Each packaged test build is created under:
  - `D:\LaptopQA\V5\dist\LaptopQATestingV5-Iteration-YYYYMMDD-HHMMSS`
  - `D:\LaptopQA\V4\dist\LaptopQATestingV4-Iteration-YYYYMMDD-HHMMSS`

The packaged folder contains:

- `Windows Laptop QA Launcher.vbs` at the root of the iteration folder. This is the silent portable Windows launcher.
- `LAPTOP QA\App\LaptopQATestingV5.exe` inside the app folder.
- Supporting folders such as `hash`, `QA sheets`, `hardware`, `logs`, and `.runtime`.

Note: during the July 10, 2026 location cleanup, V5's top-level source files were restored from an older OneDrive backup after an interrupted folder mirror. The current V5 packaged builds remain under `D:\LaptopQA\V5\dist`, but verify V5 source parity before using it for future Intune-specific development.

## Main Source Files

For V5:

- `D:\LaptopQA\V5\MainWindow.xaml`
  - Main UI layout and styling.
  - Most visual changes happen here.
- `D:\LaptopQA\V5\MainWindow.xaml.cs`
  - Main app behavior.
  - Hardware collection, QA logic, BIOS actions, diagnostics parsing, hash handling, QA sheet generation, settings, caching, and flyouts.
- `D:\LaptopQA\V5\ErrorLog.cs`
  - Writes activity/error logs into the `logs` folder.
- `D:\LaptopQA\V5\Build-LaptopQAIteration.ps1`
  - Publishes a self-contained package and creates the root launcher.
- `D:\LaptopQA\V5\Laptop-QA-Config.json`
  - Default configuration copied into output.
- `D:\LaptopQA\V5\assets\app-icon.ico`
  - App icon.

## Build A New Iteration

From PowerShell:

```powershell
dotnet build "D:\LaptopQA\V5\LaptopQATestingV5.csproj" -c Release
dotnet build "D:\LaptopQA\V5\Launcher\LaptopQALauncher.csproj" -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\LaptopQA\V5\Build-LaptopQAIteration.ps1"
```

The script outputs a JSON summary with the new iteration folder, launcher path, file count, and size. OneDrive publishing is delayed for 30 minutes so rapid rebuilds collapse into one upload. Before publishing, the latest V4 or V5 iteration is compressed and verified as a single `.zip`; only that version's newest ZIP is retained when older items can be safely removed.

As of the latest cleaned V5 builds, the package is about `76.5 MB`.

## Packaging Rules

Windows V4, Windows V5, and macOS use the package's shared `LAPTOP QA/.runtime/qa-session.json` and `LAPTOP QA/Laptop-QA-Config.json` files. Cross-platform saves are merged and replaced atomically: editable notes, final checks, and supported configuration settings stay synchronized while Windows-only cached hardware and protected settings are preserved.

The preferred package format is:

```text
LaptopQATestingV5-Iteration-YYYYMMDD-HHMMSS\
  Windows Laptop QA Launcher.vbs
  LAPTOP QA\
    App\
      LaptopQATestingV5.exe
      Start Laptop QA Local.ps1
    tools\
    hash\
    QA sheets\
    hardware\
    logs\
    .runtime\
```

Do not create ZIP packages unless specifically needed.

The root `Laptop QA.exe` launcher should be used because it works after copying the whole iteration folder to a different drive/path.

## Tools Included

Currently included in V5:

- `D:\LaptopQA\V5\tools\cctk`
  - Dell CCTK tooling for BIOS operations.
- `D:\LaptopQA\V5\tools\CommandPowerManager`
  - Dell Command Power Manager files.
- `D:\LaptopQA\V5\tools\Pnp-AudioDevices.ps1`
  - Helper for camera/audio test behavior.

Temporary Intune/Graph tools were removed until permissions are approved:

- Azure CLI was tested and removed.
- Microsoft Graph CLI was tested and removed.
- Microsoft Graph PowerShell / Azure PowerShell modules were tested and removed.

## Important Runtime Folders

Inside the packaged `LAPTOP QA` folder:

- `hash`
  - Stores generated Autopilot hash CSV files.
  - App cleans files older than 30 days.
- `QA sheets`
  - Stores QA output HTML/PNG files.
  - App cleans old HTML on launch and keeps PNGs.
  - App cleans old QA files older than 30 days.
- `hardware`
  - Stores saved hardware snapshots.
- `logs`
  - Stores activity/error logs and archived Dell diagnostics logs.
- `.runtime`
  - Stores session cache and runtime-only helper state.
  - If `.runtime` exists beside the app, keep it there.

## Major Features

The app currently supports:

- BIOS section
  - Secure Boot status/action.
  - Factory Settings button.
- Network check
  - Wi-Fi and Ethernet checks.
- Camera test
  - Launches camera workflow and restores audio settings.
- External video test
  - Manual pass/fail.
- Keyboard test
  - Launches Kris's Keyboard Tester.
- Diagnostics
  - Looks for `DellPrebootDiagnosticsLog.txt`.
  - Copies diagnostics log to `logs` with serial/date/timestamp.
  - Deletes the original diagnostics log after a verified copy.
  - Shows pass/fail status from the log.
  - If no log is found, shows manual Pass/Fail fallback buttons and explains why.
  - Raw Log button opens the diagnostics text.
- Final Checks
  - Group Tag controls.
  - Get Hash.
  - Check Hash.
  - Upload Hash.
  - Cleaned Laptop.
  - Updated Stockrooms.
  - Trackpad Working.
  - Laptop Removed From Intune.
- QA Output
  - QA Sheet.
  - ServiceNow.
- Flyout tabs
  - Notes.
  - Activity.
  - Hardware.
- Theme support
  - Light/dark mode from settings.
  - Should update live without restart.
- Startup splash
  - Shows one rotating tech joke per launch.

## Diagnostics Log Behavior

The diagnostics file is expected to be named:

```text
DellPrebootDiagnosticsLog.txt
```

Search behavior:

1. If a diagnostics folder is configured in Settings, check that folder.
2. Otherwise auto-detect a small FAT32 removable drive, usually around 50 MB.
3. If found, parse the file.
4. Copy it to `logs` as:

```text
DellPrebootDiagnosticsLog-SERIAL-YYYYMMDD-HHMMSS-fff.txt
```

5. Delete the original only after the copy is verified.

If no diagnostics log is found, the app marks Diagnostics as an error and shows fallback Pass/Fail buttons.

## QA Sheet Output

The QA output should keep the clean V2/V4-inspired report style:

- Header with overall status.
- Device identity.
- Condensed hardware specs.
- QA results table.
- Notes section.
- Diagnostics result is included in the QA Results table.
- Do not include the camera roll location at the bottom.
- ServiceNow button currently opens the request and copies info to clipboard. Complex ServiceNow auto-fill was attempted and intentionally backed out because only the description field worked reliably.

## Intune / Graph Status

Intune automation is currently disabled until permissions are approved.

The UI/hooks may still exist, but the packaged CLI tools were removed to keep the app small.

There is no active MSAL token cache in current V5 because the active MSAL/Graph sign-in paths were removed while Intune permissions are pending. If Intune is re-enabled with MSAL, use a DPAPI-protected MSAL token cache on Windows rather than a plaintext token file.

Attempts made:

- Microsoft Graph PowerShell
  - Blocked by RELX Conditional Access.
  - Error `53003`.
- Azure PowerShell / `Az.Accounts`
  - Blocked by RELX Conditional Access.
  - Error `53003`.
- Packaged Azure CLI
  - Basic sign-in worked.
  - Graph call failed with missing Intune scope.
  - Requesting Intune scope failed with `AADSTS65002` / preauthorization required.
- Microsoft Graph CLI
  - Packaged and tested.
  - Correct permission name was `DeviceManagementServiceConfig.Read.All`.
  - Ultimately blocked by RELX Conditional Access.
  - Error `53003`.

Recommended future path:

Ask an admin for an approved app registration for Laptop QA with appropriate Microsoft Graph delegated permissions, likely:

- `DeviceManagementServiceConfig.Read.All`
- `DeviceManagementServiceConfig.ReadWrite.All`
- `DeviceManagementManagedDevices.ReadWrite.All`

Then wire V5 back to the approved app/client ID flow.

## Dell Warranty

Current V4/V5 behavior uses Dell Command | Warranty instead of storing Dell API credentials in the app.

- The packaged CLI lives under `tools\DellWarranty\DellWarranty-CLI.exe`.
- The app writes the current Service Tag to a temporary CSV, runs the CLI, reads the output CSV, and uses the latest `End Date`.
- `Laptop-QA-Config.json` only has an optional `DellWarrantyCliPath` override. Leave it blank to use the packaged tool or the system-installed Dell Command | Integration Suite copy.

When auditing a package, search for old credential markers before sharing:

```powershell
Select-String -Path "D:\LaptopQA\V5\dist\LATEST\LAPTOP QA\**\*" -Pattern "client_secret","shared secret","DellWarrantyClientSecret" -SimpleMatch
```

Expected result: no warranty API credentials.

## BIOS Notes

Dell CCTK is located in:

```text
D:\LaptopQA\V5\tools\cctk
```

Known notes:

- Secure Boot can be read/written with CCTK on supported Dell systems.
- Factory Settings should mean Dell's Factory Defaults, not BIOS Defaults or Last Known Good.
- Be careful with factory defaults because it can alter boot behavior and may cause imaging/recovery issues if a USB drive is still attached.
- Primary AC was removed from both V4 and V5 after Dell Optimizer / Command Power Manager access issues.

## Power Menu Notes

SupportAssist boot selector attempts were removed after unreliable behavior.

Current options should include only the paths that still work or are intentionally kept:

- Shutdown.
- Reboot.
- Reboot to BIOS.
- Windows Recovery.

If changing boot behavior, test on a real Dell laptop and confirm it returns to Windows correctly.

## Styling Guidelines

The app intentionally follows a V2/V4 visual style:

- Dark teal glass-like panels.
- Rounded app corners and large soft shadow.
- Right-side folder-style flyout tabs.
- Standard action buttons are typically `82 x 34`.
- Final Checks should be one single section, not individual boxed rows.
- The small check square remains, but there should not be a separate bubble around each final-check row.
- Diagnostics Browse/Raw Log buttons and QA Output buttons should match standard action button sizing.
- Avoid making the app always-on-top after the splash screen. Splash may be topmost during loading only.

## Common Maintenance Tasks

### Add or edit loading jokes

Edit `StartupLoadingJokes` near the top of:

```text
D:\LaptopQA\V5\MainWindow.xaml.cs
```

The app shows one joke per launch and rotates through the list using `.runtime\startup-joke-index.txt`.

Keep jokes short enough to fit the splash card.

### Change final checks

Edit Final Checks layout in:

```text
D:\LaptopQA\V5\MainWindow.xaml
```

Also update QA output rows in:

```text
D:\LaptopQA\V5\MainWindow.xaml.cs
```

Search for:

```text
BuildQaRows
Hash and group tag
FinalCleanedLaptopCheck
FinalUpdateStockroomsCheck
FinalTrackpadWorkingCheck
FinalDeletedUserCheck
```

### Change QA sheet content

Search in `MainWindow.xaml.cs` for:

```text
GenerateQaSheet
BuildQaRows
QA RESULTS
```

Keep the output clean and condensed.

### Change diagnostics parsing

Search in `MainWindow.xaml.cs` for:

```text
ParseDiagnosticsLog
CondenseDiagnosticsFailure
HumanizeDiagnosticsText
```

Diagnostics failures should be plain human-readable text and condensed enough to fit the main UI. Long text can scroll in the diagnostics detail area.

### Change theme colors

Search in `MainWindow.xaml.cs` for:

```text
ThemePalette
ApplyAppTheme
```

Search in `MainWindow.xaml` for resource keys like:

```text
ShellBrush
GlassPanelBrush
PrimaryButtonBrush
MutedBrush
TextBrush
```

## Testing Checklist Before Sharing A Build

1. App launches and splash screen appears on top.
2. After loading, app is not always-on-top.
3. Light/dark mode switch works live.
4. Network check runs twice reliably.
5. Hardware flyout opens, copies, and saves.
6. Diagnostics:
   - Log found path works.
   - No-log fallback Pass/Fail buttons appear.
   - Detail text scrolls if too long.
7. QA Sheet opens and PNG/HTML output is generated.
8. ServiceNow opens request and copies info to clipboard.
9. Power menu options still work.
10. Package size is reasonable.
11. `logs` folder receives errors/activity when failures occur.
12. No readable secrets are present in the package.

## If Something Breaks

First check:

```text
LAPTOP QA\logs
```

Then check:

```text
LAPTOP QA\.runtime
```

If startup state looks stale, try deleting:

```text
LAPTOP QA\.runtime\qa-session.json
```

Do not delete user output folders unless intentionally resetting test data:

- `hash`
- `QA sheets`
- `hardware`
- `logs`

## Current Known Limitations

- Intune automation is paused until admin permissions/app registration are available.
- ServiceNow auto-fill is intentionally limited because reliable field automation was not achieved.
- BIOS operations require supported Dell hardware and may require admin context.
- Dell warranty lookup depends on Dell Command | Warranty CLI network access.
- Some Dell tooling behavior can vary by model and firmware version.
