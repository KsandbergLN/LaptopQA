# Canonical source marker

This repository root is the canonical Windows Laptop QA source as of 2026-08-11. Clone it anywhere; no fixed drive or parent-folder path is required.

Repository scope includes the local `Shared`, launcher, drive-marker, and `tools\Start-OneDriveDebouncedSync.ps1` files. Windows OneDrive deployment reads accepted packages from this repository's `dist` folder; the separate macOS build/sync workflow remains unchanged.

Do not edit `bin`, `obj`, `dist`, or separately stored historical/recovery copies. Build candidates with `Build-LaptopQAIteration.ps1`; accept them with `Approve-LaptopQAPackage.ps1`; deploy them with `Deploy-LaptopQAPackage.ps1`.

Laptop QA is a guided technician workflow for preparing, testing, and documenting Windows laptops. It combines hardware and diagnostics checks, BIOS and USB workflows, device-condition checks, device-hash upload, final-check links, QA-sheet output, and ServiceNow preparation, with a macOS companion for continuing the cached Windows session from step 8.
