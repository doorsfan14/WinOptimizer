# Architecture

## Current shape

The initial release is a single WPF executable (`src/WinOptimizer.App`) with clear source boundaries instead of multiple projects that would add packaging and dependency overhead before there are stable module contracts:

- `Models` contains immutable telemetry and optimization records.
- `Services` holds metric math, hardware polling, local persistence/logging, recommendations, scoped cleanup, and startup backup/apply/restore.
- `ViewModels` owns page state, navigation, setup, and the background sampling loop.
- `Views` and `Controls` define the WPF shell, screens, and vector gauges/charts.
- `tests` tests pure metric, history, recommendation, visualization mapping, and website behavior without requiring actual hardware readings.

The app is built for `net10.0-windows` with WPF. This is a Windows desktop product; the GitHub Pages site is a small standalone HTML/CSS/JavaScript page. `System.Management` is used only for discovery where Windows exposes it. CPU/memory use documented Win32 performance interfaces, storage uses `DriveInfo`, power scheme uses the power-plan API, and hardware sensors come from LibreHardwareMonitor.

## Sampling and data flow

The shell starts a cancellable asynchronous sampling loop using the configured polling interval. One snapshot is shared with visible view models; expensive process inventory refresh runs only on the Performance page. Histories are bounded to 300 readings and a selected time window. Hardware readings are nullable and retain source, sensor name/type, and unit metadata. No placeholder values are injected into production telemetry.

Local preferences and activity history are JSON under `%LOCALAPPDATA%\WinOptimizer`; history is capped at 300 entries. Logging is local, capped, and rotated. No data leaves the machine.

## Threading and lifetime

Sampling occurs off the UI thread. View models marshal observable updates to the WPF dispatcher. Cancellation is tied to the shell/window lifetime. WPF custom controls render charts and indicators from bounded data; animation is data-driven, capped, and honors the application's reduced-motion behavior.

## System changes

Changes are deliberately limited in this first version to non-elevated user startup entries and a preview-first cleanup of eligible files in the current user's Temp folder. Each operation carries its scope and backups and checks the after-state. Machine-level settings and privileged maintenance remain out of scope until a reviewed least-privilege design is implemented.

## Build and runtime

The app targets .NET 10 WPF and `win-x64`. Microsoft documents Windows 11 and Windows 10 Enterprise/LTSC editions as supported for .NET 10; Home/Pro are outside that support matrix. CI runs on Windows to validate WPF compilation/tests. For local packaging see `scripts/build.ps1` and `installer/WinOptimizer.iss`.

## Deliberate non-goals

No kernel driver, fan control, service disabling, arbitrary registry bundle, boot/network tweak, remote executor, auto-updater, optimizer daemon, telemetry upload, fake benchmark, or “health score” is included. Future projects should keep these boundaries or justify changes with a threat model, measurement, and recovery story.
