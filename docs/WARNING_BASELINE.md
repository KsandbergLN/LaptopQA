# Compiler warning baseline

Captured on 2026-08-11 with .NET SDK 10.0.302:

- Handoff document: 38 warnings, 0 errors.
- Reproduced canonical source: 33 warnings, 0 errors.
- Current accepted baseline: 0 warnings, 0 errors.

The corrected warnings were nullable-flow findings across hardware UI, keyboard UI, configuration, diagnostics, USB handling, session caching, and the main workflow. `TreatWarningsAsErrors` now prevents warning growth.

Verification command:

```powershell
dotnet build .\LaptopQATestingV4.csproj -c Release --no-incremental
```
