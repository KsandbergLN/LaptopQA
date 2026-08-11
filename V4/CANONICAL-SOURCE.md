# Canonical source marker

This directory is the canonical Windows Laptop QA source as of 2026-08-11.

Repository scope also includes `..\Shared`, `..\Start-LaptopQA-Local.ps1`, `..\Start-LaptopQA-Silent.vbs`, `..\Laptop-QA-Drive.json`, and `..\tools\Start-OneDriveDebouncedSync.ps1`.

Do not edit `bin`, `obj`, `dist`, or any copy under `C:\V2`. Build candidates with `Build-LaptopQAIteration.ps1`; accept them with `Approve-LaptopQAPackage.ps1`; deploy them with `Deploy-LaptopQAPackage.ps1`.
