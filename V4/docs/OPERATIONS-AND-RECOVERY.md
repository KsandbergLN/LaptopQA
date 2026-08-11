# Operations and recovery

## Hardware qualification

Record every supported model in `DEVICE-MATRIX.csv`. A model is supported only after startup, battery, BIOS, diagnostics, camera/audio restoration, external display, USB topology, QA output, ServiceNow fallback, and approved power actions have evidence. Re-test after BIOS, Windows, dock firmware, or bundled-tool changes.

## Safe administrator-action procedure

1. Use a disposable QA laptop connected to AC power; never validate BIOS reset or power actions on a developer workstation.
2. Record service tag, asset tag, BIOS settings, BitLocker recovery-key escrow status, boot mode, Secure Boot state, and current package manifest before testing.
3. Disconnect production removable media. Use labeled test media with backed-up contents.
4. Test read-only collection first. Execute BIOS writes, reboot, shutdown, or recovery actions only with a second technician present.
5. Confirm the device returns to Windows, networking works, the data root is correct, and the QA cache/logs remain readable.

## Recovery procedure

1. Stop testing after an unexpected BIOS, boot, storage, or encryption result.
2. Capture the application activity log, Windows event logs, package manifest, device/BIOS details, and the exact action taken.
3. Restore the recorded BIOS settings. Use the escrowed BitLocker recovery key if prompted.
4. Boot the approved recovery environment or last-known-good OS image. Do not improvise firmware changes.
5. Restore the last accepted package only after verifying its manifest hashes.
6. Mark the device/model combination failed in `DEVICE-MATRIX.csv` and require review before resuming use.

## ServiceNow fallback

The application opens the configured request URL and copies the request description as plain text. The technician must paste it into the description field, review assignment/type fields, and submit manually. Browser focus, window-title matching, timing, clipboard-injected JavaScript, and automatic submission are not part of the supported path.
