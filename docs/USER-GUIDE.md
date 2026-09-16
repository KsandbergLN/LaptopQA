# Laptop QA user guide

This guide is for technicians using the supported Windows app and macOS companion. Both apps support the Windows QA workflow; the macOS companion uses the cached session so the workflow can continue on a MacBook when preferred.

Laptop QA prepares, tests, and documents Windows laptops in one guided session, covering hardware and diagnostics checks, BIOS and USB workflows, device-condition checks, battery-health reporting, device-hash upload, final-check links, QA-sheet output, and Create Ticket preparation.

## Windows workflow

1. On Windows, double-click `Windows Laptop QA Launcher.vbs` in the approved package. The launcher starts the app and keeps the package data folders together. Do not launch the internal executable directly for normal technician use.
2. Enter or confirm the technician name and review the device identity.
3. Run Windows steps 1–7: battery, BIOS, diagnostics, camera/audio, display, keyboard, USB ports, storage, and other device-specific checks. When those steps are complete, Laptop QA confirms that the session was saved and reminds you to move the QA USB drive to your own PC if you want to finish the remaining workflow there.
4. For USB testing, follow the on-screen port indicators and move a readable test drive between the requested physical ports. Do not count a dock as a laptop port unless the workflow says to do so. Use **Retest** in the USB Port Test card to clear every port result and restart the full USB test when a result needs to be repeated. The steps 1-7 reminder appears only after Laptop QA detects the expected USB-port count and every detected port has a result.
5. Mark each step Pass, Fail, or Ignore only when the device matrix or test procedure allows it. Add notes for failures, exceptions, or administrator actions.
6. Complete **8. Device Condition** by marking **Trackpad Working** and **Checked Physical Condition** Pass or Fail.
7. Complete **9. Final Checks** using the configured Intune and ServiceNow links. The final checks include removing the user, checking the hash and group tag, updating stockrooms, and confirming the laptop was cleaned.
8. Use **10. QA Output** to generate the QA sheet. Its final battery-health row passes Excellent or Good, fails Fair or Poor, and records the Dell diagnostics rating plus Windows capacity percentage in the detail column. If the Dell rating is unavailable, the sheet reports it as unavailable rather than inferring health from the current charge level. Keep the package's `QA sheets`, `logs`, `hardware`, and `activity` folders with the session data.
9. Select **Create Ticket** to open the configured Generic Service Request. Laptop QA attempts to fill the request type, assignment group, and description automatically. After the attempt, the QA summary is copied to the clipboard for review or manual paste. Confirm all ServiceNow fields before submitting. If the automation cannot start, the app opens the request and leaves the same QA summary on the clipboard for manual completion.
10. Select **Export Hash** to collect and save the Windows Autopilot hardware hash. Select **Upload Hash** to open the configured Intune Autopilot Devices page in a new tab for hash upload. Select **Check Hash and Group Tag** to open Intune Windows Devices in a new Edge tab. The service tag is copied to the clipboard for a manual device search. Verify the device and group tag in Intune, then select the adjacent checkbox to mark the step complete.
11. Select **Remove User from Laptop in Intune** to open Intune Autopilot Devices in a new Edge tab. The service tag is copied to the clipboard. Complete the removal, then select the adjacent checkbox to mark the step complete.
12. Select **Update Stockrooms** to open the current laptop's ServiceNow hardware list in a new Edge tab. Laptop QA attempts to select **Serial number**, enter the service tag, and run the search. The service tag remains copied to the clipboard; if the page does not update, select **Serial number**, paste the service tag, and press Enter. Update the applicable stockroom record, then select the adjacent checkbox to mark the step complete.
13. Open **Settings** to change any final-check destination under **Final Check Links**. The Update Stockrooms URL must retain `{SERIAL}` where the active laptop's service tag belongs. Windows also uses the **Upload Hash URL** after **Export Hash**. The macOS companion reads the shared links and supports the three cached final-check actions (Check Hash and Group Tag, Remove User, and Update Stockrooms); it does not collect or upload the Windows hash. Its buttons open the configured pages and copy the relevant information to the clipboard; they do not fill or submit forms.

### Settings and configuration

Use **Settings** for technician preferences and approved organization values. Technician name, language, and theme affect the header, session, and QA sheet. **Camera Roll folder** points the camera test and cleanup at the correct folder, and **Dell diagnostics log folder** is an optional location for preboot logs and retained source archives. The device-name format accepts `{serial}`, `{computer}`, and `{asset}`. The Autopilot group tag is the expected tag used when the exported hash is checked.

The Wi-Fi/Ethernet and Camera Roll timing fields control retry and rescan waits; leave them at the approved defaults unless the device procedure requires a change. ServiceNow settings identify the request page, request type, assignment group name and ID, and the browser-automation wait. If browser automation cannot start, Laptop QA still opens the request and copies the QA summary for manual completion. Final-check URLs are destinations, not credentials—never store passwords or access tokens in the configuration. **Reset Settings** restores defaults and removes the saved technician name without deleting QA sheets, logs, hardware files, or session history.

The current Windows layout is: steps 1–7 for Windows hardware and diagnostics, step 8 for device condition, step 9 for final checks, and step 10 for QA output.

The Windows app can be resized from any edge or corner while restored; its layout scales together and keeps the same proportions. Use the title-bar controls to minimize, maximize or restore, and close the app. Maximized mode uses the normal usable desktop area.

### Diagnostics log behavior

Laptop QA prefers a fresh `DellPrebootDiagnosticsLog.txt`. If that is unavailable, it accepts one unambiguous retained log or a retained filename containing the active service tag; it does not guess among multiple unmatched retained logs. Windows checks configured locations and connected small FAT32 diagnostics media during startup when the restored session has no cached log. The original source log is retained with service-tag/timestamp naming. On macOS, the companion can load an unambiguous missing log from mounted FAT32 diagnostics media, use **Browse** for manual selection, and show the selected text with **Raw Log**. An existing cached result is preserved when no new unambiguous log is available.

### When the session saves

Laptop QA automatically saves the active session shortly after a button click or text change, once the session is ready. It also saves during startup, when the steps 1–7 handoff is reached, before resetting to start a new QA, and when the app closes. The active session is stored in `.runtime/qa-session.json`; searchable snapshots are kept under `.runtime/sessions` for 90 days. Keep those folders with the package so the macOS companion can load the cached Windows session.

## macOS companion

Starting with step 8 (Device Condition), the technician may move to their personal Mac and open the macOS companion. Load the cached Windows session and continue the same Windows QA workflow there: review diagnostics, complete device-condition checks and final checks, generate the step 10 QA sheet, and prepare Create Ticket details. Windows-only operations such as BIOS changes, hardware hash collection/upload, USB port scoring, battery/diagnostics collection, and steps 1–7 must be completed in the Windows app before switching to the Mac.

The personal Mac should use the same approved package/session data location or removable drive so the cached Windows results are available. Open `macOS Laptop QA Launcher.app` to start the companion. The macOS companion is an alternate workstation for the Windows QA workflow; it does not add a separate Mac-specific checklist or replace the required Windows hardware checks. Its battery display and QA sheet use the cached Windows capacity percentage plus the Dell diagnostics health rating.

## Data and safety

Use an approved package location and keep the package's data folders together.

When a Dell preboot diagnostics log reports that a technician did not respond to an interactive check, the Diagnostics section and QA sheet identify the prompt category when available, such as Video, Audio, Camera, Keyboard, or pointing device. Review that named prompt before completing the QA.
