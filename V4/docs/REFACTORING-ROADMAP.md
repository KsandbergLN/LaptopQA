# Incremental MainWindow extraction roadmap

`MainWindow.xaml.cs` remains the largest maintenance risk. Refactoring must preserve behavior and be performed in reviewed, independently buildable steps.

1. **Mechanical partial-class split:** move existing regions into `MainWindow.Startup.cs`, `MainWindow.Hardware.cs`, `MainWindow.Diagnostics.cs`, `MainWindow.Usb.cs`, `MainWindow.Output.cs`, and `MainWindow.Session.cs` without logic changes.
2. **Process runner:** introduce an injectable process/PowerShell runner and characterize exit-code, timeout, cancellation, and logging behavior.
3. **Configuration/data root:** extract config serialization, atomic writes, shared-data-root selection, and migration tests.
4. **Diagnostics:** extract path discovery, parsing, archive verification, and failure condensation behind a service with fixture tests.
5. **QA output:** extract row construction, HTML rendering, filename cleanup, and manifest-aware output handling.
6. **Hardware/USB:** extract read-only collection first; keep BIOS writes and scoring unchanged until device-matrix coverage exists.

Each step requires a zero-warning build, focused automated tests, the manual smoke checklist, and a reviewed branch. Do not combine a mechanical move with behavior changes.
