# Changelog

This file tracks notable user-facing changes.

## 26.0.0 — Preview prerelease

- Adds a custom Windows title bar, guided recommendation review, and a direct handoff to Windows Optional Features.
- Corrects binary byte units (KB, MB, GB, TB) and tests their boundaries.
- Adds links to official companion downloads: Microsoft Sysinternals Autoruns and Process Explorer, and WizTree.
- Adds a download of the current release workflow as a per-user installer and portable archive when the GitHub prerelease job completes.
- Memory remains diagnostic; no working-set “RAM cleaner” is included because it can increase reloads and disk activity.
- Known limits: sensor coverage depends on firmware; no code-signing certificate is configured; Windows feature choices are managed by Windows and remain the user's responsibility.

## Unreleased

- Bundled Plus Jakarta Sans (SIL OFL) and refreshed the desktop palette, navigation status, and Optimize experience.
- Added an AC-only, verified High performance plan action with conflict-aware History restoration.
- Added confirmed Windows media-aware drive maintenance for one selected fixed volume; progress and results come from the Windows tool.
- Reduced monitoring overhead with a two-second polling minimum and a five-second hardware sensor refresh cap.
- Expanded tests for power-plan eligibility and strict drive argument validation.
- Initial WPF desktop application with local telemetry, CPU and memory charts, hardware/sensor views, and first-run setup.
- Capability-checked LibreHardwareMonitor sensor discovery; unsupported readings stay unavailable.
- Read-only hardware-aware recommendations, preview-first user Temp cleanup, and reversible current-user startup entry management.
- Local settings/activity history, system/light/dark themes, GitHub Pages site, tests, and Windows packaging recipes.
- Windows build and automated tests were run locally; physical sensor coverage and performance gains remain unverified and are not promised.

