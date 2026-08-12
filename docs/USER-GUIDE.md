# Laptop QA user guide

This guide is for technicians using the supported Windows app and macOS companion. Both apps support the Windows QA workflow; the macOS companion uses the cached session so the workflow can continue on a MacBook when preferred.

## Windows workflow

1. Start `LaptopQA.Windows.exe` from the approved package or use the provided launcher.
2. Enter or confirm the technician name and review the device identity.
3. Run the displayed QA steps: battery, BIOS, diagnostics, camera/audio, display, keyboard, USB ports, storage, and other device-specific checks.
4. For USB testing, follow the on-screen port indicators and move a readable test drive between the requested physical ports. Do not count a dock as a laptop port unless the workflow says to do so.
5. Mark each step Pass, Fail, or Ignore only when the device matrix or test procedure allows it. Add notes for failures, exceptions, or administrator actions.
6. Review the summary and generate the QA sheet. Keep the package's `QA sheets`, `logs`, `hardware`, and `activity` folders with the session data.
7. Select **ServiceNow** to open the configured Generic Service Request. Laptop QA attempts to fill the request type, assignment group, and description automatically. After the attempt, the QA summary is copied to the clipboard for review or manual paste. Confirm all ServiceNow fields before submitting. If the automation cannot start, the app opens the request and leaves the same QA summary on the clipboard for manual completion.

## macOS companion

Starting with step 8 (QA Output), the technician may move to their personal Mac and open the macOS companion. Load the cached Windows session and continue the same Windows QA workflow there: complete the final checks, generate the QA sheet, and prepare ServiceNow details. Windows-only operations such as BIOS changes, hardware hash collection, USB port scoring, and steps 1–7 must be completed in the Windows app before switching to the Mac.

The personal Mac should use the same approved package/session data location or removable drive so the cached Windows results are available. The macOS companion is an alternate workstation for the Windows QA workflow; it does not add a separate Mac-specific checklist or replace the required Windows hardware checks.

## Data and safety

Use an approved package location and keep the package's data folders together.
