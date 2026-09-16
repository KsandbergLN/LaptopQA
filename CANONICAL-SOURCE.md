# Canonical source marker

This repository root is the canonical Laptop QA source as of 2026-09-16. Clone it anywhere; no fixed drive or parent-folder path is required. The `main` branch is authoritative; reviewed feature branches and pull requests are the change path.

Repository scope includes the Windows app, the macOS companion under `macos\`, shared `Shared` resources, launchers, the drive marker, and `tools\Start-OneDriveDebouncedSync.ps1`. Windows OneDrive deployment reads accepted packages from this repository's `dist` folder; the macOS build/sync workflow remains separate but uses the same cached session and configuration model.

Do not edit `bin`, `obj`, `dist`, runtime data, or separately stored historical/recovery copies. Build candidates with `Build-LaptopQAIteration.ps1`; audit and quarantine incomplete output with `Audit-LaptopQAPackages.ps1`; accept them with `Approve-LaptopQAPackage.ps1`; deploy them with `Deploy-LaptopQAPackage.ps1`. Use `-NoDeploy` for build-only work and `-WhatIf` before any state-changing deployment rehearsal.

Laptop QA is a guided technician workflow for preparing, testing, and documenting Windows laptops. It combines hardware and diagnostics checks, BIOS and USB workflows, device-condition checks, battery-health reporting, device-hash upload, final-check links, QA-sheet output, and Create Ticket/ServiceNow preparation. The macOS companion continues the cached Windows session from step 8 on a personal MacBook; it does not add a Mac-specific hardware checklist.

The current accepted baseline is represented by the `laptopqa-windows-2026.09.15` and `laptopqa-macos-2026.09.15` tags. See `DEVELOPER-HANDOFF.md` for build, configuration, smoke-test, recovery, and ownership-transfer details; see `docs/USER-GUIDE.md` for technician operation.
