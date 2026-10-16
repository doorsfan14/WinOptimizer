# Contributing

Thanks for helping make system maintenance understandable and safe. Contributions should be small enough to review and should keep the app useful when hardware telemetry is incomplete.

## Before opening an issue or pull request

- Search existing reports, then include Windows edition/build, device model, and reproducible steps.
- For sensor reports, include the displayed sensor name/type and source if known. Remove usernames, serial numbers, local paths, and other identifying details from screenshots/logs.
- Do not upload registry exports, memory dumps, documents, crash dumps, or credentials.
- For system-changing work, describe eligibility, preview text, permissions, backup, verification, and undo/conflict behavior before implementation.

## Development

Use the .NET 10 SDK on Windows, a recent Node.js runtime, and Inno Setup 6 when packaging. Build/test with:

```powershell
dotnet test .\tests\WinOptimizer.Tests\WinOptimizer.Tests.csproj
node .\tests\WinOptimizer.Tests\website.test.mjs
```

The WPF project needs Windows targeting to execute. Keep unit tests independent of actual sensors and machine modifications; tests should inject values and must not change the host PC. Do not add hardware-dependent expected values.

## Code and product standards

- Use documented Windows APIs and maintained libraries. Check telemetry for capability, type, source, timestamp, and valid range before displaying it.
- Keep polling asynchronous and bounded. Dispose subscriptions/timers and avoid disk writes in the sampling loop.
- Treat missing data distinctly from zero. Never add production demo readings or claim a recommendation improved performance without a repeatable comparison.
- Any mutation must be individually explained and user-confirmed, use least privilege, back up the precise value, verify the postcondition, and provide restoration where possible.
- Do not disable security protections or critical Windows services, terminate processes as a memory tweak, or add opaque registry/boot/network “tweaks.”
- Keep keyboard interaction, visible focus, accessible names, responsive layout, and light/dark/System themes in view changes.
- Keep dependencies small and document third-party licensing.

Pull requests should explain the user-facing change, risk, validation performed, and any Windows/hardware cases not exercised. Never claim a Windows build passed unless the workflow or a local Windows build did pass.

