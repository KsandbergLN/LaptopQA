# Laptop QA user guide

This guide is for technicians using the supported Windows app and macOS companion. The Windows app is the source of truth for hardware checks and the QA session; the macOS app works with the cached session and Mac-specific notes.

## Windows workflow

1. Start `LaptopQA.Windows.exe` from the approved package or use the provided launcher.
2. Enter or confirm the technician name and review the device identity.
3. Run the displayed QA steps: battery, BIOS, diagnostics, camera/audio, display, keyboard, USB ports, storage, and other device-specific checks.
4. For USB testing, follow the on-screen port indicators and move a readable test drive between the requested physical ports. Do not count a dock as a laptop port unless the workflow says to do so.
5. Mark each step Pass, Fail, or Ignore only when the device matrix or test procedure allows it. Add notes for failures, exceptions, or administrator actions.
6. Review the summary and generate the QA sheet. Keep the package's `QA sheets`, `logs`, `hardware`, and `activity` folders with the session data.
7. Use the ServiceNow action as a best-effort helper. If browser automation is unavailable, use the copied request details and complete the form manually.

## macOS companion

The macOS companion can open the cached Windows session, add Mac-specific checks and notes, generate a QA sheet, and prepare ServiceNow details. Windows-only operations such as BIOS changes, hardware hash collection, and USB port scoring must be completed in the Windows app.

## When a check fails

Record the exact symptom, port or device involved, visible error text, and any administrator action. Do not overwrite an accepted package. Escalate hardware or firmware variation using `docs/DEVICE-MATRIX.csv` and follow `docs/OPERATIONS-AND-RECOVERY.md` for recovery.

## Data and safety

Use an approved package location and keep the package's data folders together. Do not edit files under `bin`, `obj`, or `dist` as source. Do not deploy to OneDrive or a removable drive unless the package has been reviewed and accepted.
