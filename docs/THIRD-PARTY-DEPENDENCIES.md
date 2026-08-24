# Third-party dependency register

Review date: 2026-08-11

| Component | Bundled version evidence | Primary bundled file SHA-256 | Authoritative source | License/provenance status |
| --- | --- | --- | --- | --- |
| Dell Command \| Configure | `cctk.exe` reports 5.2.2.0; Dell package `F2V9N`, 5.2.2.292 A00 | `CBFE142C9DB3B41D02AE7B41530818516FE2F481408880718F18A61F20F469F0` | https://www.dell.com/support/home/en-us/drivers/DriversDetails?driverId=F2V9N | Extracted from Dell's SHA-256 verified package on 2026-08-24. Bundle the complete `tools\\dell-command-configure` runtime; do not substitute the retired CCTK 2.1 files. |
| Dell Command Power Manager | 3.9.0.11 | `F2C0302BDD2FB35778B4479D3C649A6EB240EBB1440FD63F8DCFC0642F1F3660` | https://www.dell.com/support/product-details/en-us/product/dell-command-power-manager/drivers | Bundled `readme.txt` retained; archive the exact Dell installer and terms internally. |
| Dell Command Warranty / Integration Suite | 6.7.1.44 | `48392C93E504BA0634E86C13372702E271CC9C23DFE45594F2C2AA7209D9ACDC` | https://www.dell.com/support/kbdoc/en-us/000178049/dell-command-integration-suite-for-microsoft-system-center | Bundled `readme.txt` retained; verify redistribution against Dell terms. |
| Get-WindowsAutoPilotInfo.ps1 | 3.9 (embedded `.VERSION`) | `60C13CF3A63E0E38D41D075C85E95C7EB4A8EF301E0C605DE6DE29E5A552170D` | https://www.powershellgallery.com/packages/Get-WindowsAutoPilotInfo/3.9 | Version is pinned; do not replace the file without hash review and a hash-generation smoke test. |
| Pnp-AudioDevices.ps1 | Internal/local helper; no embedded version | `7FF17C6F4DB56C9870DB2A5DF57F2F4C0813B382754EC6DC48F795BE510B7EF4` | Internal source required | Owner and license are not recorded; resolve before external redistribution. |

## Controlled update process

1. Open a reviewed dependency-update branch.
2. Download only from the authoritative source and retain the original installer, release notes, license terms, published checksum, and download date in the approved internal artifact store.
3. Verify publisher signatures and SHA-256 hashes before extraction.
4. Update this register and record every changed bundled-file hash.
5. Run a zero-warning Release build and create a manifested candidate package.
6. Test BIOS reads/writes, battery mode reads, warranty lookup, audio restoration, and Autopilot hash generation on the supported-device matrix.
7. Approve and deploy only after test evidence is attached to the package manifest.
