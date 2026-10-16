# WinOptimizer

**Clear, hardware-aware insight into your Windows PC.** WinOptimizer combines live system monitoring with cautious, reviewable maintenance tools. It is an early open-source project; measurements and recommendations are shown only when the relevant Windows interface reports them.

> **Development status:** the Windows app and automated tests build locally, but no installer has been released yet. Sensor coverage still needs validation across physical PCs. The GitHub Pages site intentionally links to the repository and releases page rather than claiming a download exists.

## What works in this first implementation

- Live CPU utilization, physical and committed memory, fixed-volume capacity, disk activity, power plan, process resource use, and capability-checked hardware telemetry.
- CPU gauge, bounded metric history charts, memory and storage explanations, and a vector fan display that rotates only when a real RPM sensor reports a value. Missing, warming-up, zero, and unavailable readings remain distinct.
- Read-only first-run discovery and preferences, with Safe recommendations selected by default.
- Startup inventory. Only current-user Run/RunOnce entries are offered for change; elevated machine entries and folder shortcuts remain read-only. Changes are backed up, confirmed, verified, recorded, and restorable when the original state is still compatible.
- A preview-first cleanup limited to old top-level temporary/cache files in the signed-in user's Temp folder. It excludes folders, newer files, and paths outside that scope.
- Local settings, bounded activity history, local rolling logs, and actual light/dark/system theme switching.

See [architecture](docs/ARCHITECTURE.md), [hardware and telemetry](docs/HARDWARE.md), and [optimization safety](docs/OPTIMIZATION_SAFETY.md) for implementation boundaries and limitations.

## Requirements

- 64-bit Windows and the .NET 10 Desktop Runtime. The first package target is `win-x64`.
- Microsoft currently lists Windows 11 and Windows 10 Enterprise/LTSC editions in the .NET 10 Windows support matrix. Windows 10 Home/Pro may be able to run the application but are outside that runtime support listing; see [Microsoft's Windows support matrix](https://learn.microsoft.com/dotnet/core/install/windows).
- Standard-user access is sufficient for normal monitoring and user-level startup management. Some sensors or system-level readings can be unavailable without vendor support or elevated access; WinOptimizer does not elevate itself.
- Fan, temperature, GPU, drive-health, and some frequency readings depend on firmware, drivers, device support, and the available sensor interface.

## Build and run

Install the .NET 10 SDK and Windows Desktop development workload/runtime, then from the repository root run:

```powershell
dotnet restore .\tests\WinOptimizer.Tests\WinOptimizer.Tests.csproj --configfile .\NuGet.Config
dotnet test .\tests\WinOptimizer.Tests\WinOptimizer.Tests.csproj
dotnet run --project .\src\WinOptimizer.App\WinOptimizer.App.csproj
```

To publish an x64 app folder and create a per-user setup executable (after installing Inno Setup 6):

```powershell
.\scripts\build.ps1 -Configuration Release -Package
```

The installer does not request administrator rights. It installs under the current user's local Programs directory and preserves settings in `%LOCALAPPDATA%\WinOptimizer` during upgrade/uninstall. No code signing certificate is configured, so Windows may show the standard publisher warning for externally downloaded binaries.

This repository targets .NET 10 WPF because it provides a native Windows desktop UI and current runtime support. `LibreHardwareMonitorLib` supplies supported hardware sensor discovery; its upstream license is MPL-2.0 and its notices must remain with redistributed binaries. See [dependency notes](docs/HARDWARE.md#sensor-library).

## Privacy

WinOptimizer has no analytics, ads, cloud upload, updater, or background persistence. System snapshots and activity history stay in the signed-in user's local app-data folder. Logs may include exception detail and local paths; see [SECURITY.md](SECURITY.md) before sharing one.

## Project files

- `src/WinOptimizer.App` — WPF application, Windows telemetry, view models, user-level maintenance.
- `tests/WinOptimizer.Tests` — metric, history, sensor-validation, recommendation, visualization, and website checks.
- `index.html` — lightweight GitHub Pages project site.
- `installer` and `scripts` — Inno Setup recipe and Windows build/package script.
- `docs` — design decisions, telemetry boundaries, and safety model.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and the safety requirements before changing system settings. Hardware-specific behavior needs reproducible model/OS details and measurements with their sensor source. Do not include personal files, memory dumps, registry exports, or secrets in issues.

## License

WinOptimizer is distributed under the [GNU General Public License v3.0 or later](LICENSE). Third-party component licenses remain applicable to those components.
