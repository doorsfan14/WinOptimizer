# Optimization safety model

WinOptimizer is a maintenance assistant, not an automatic “tweak” engine. High resource usage is not automatically a defect, and an optimization is never considered beneficial without before/after evidence under comparable conditions.

## Operation lifecycle

Every system-changing recommendation should describe its identifier/category, finding, eligibility, target, expected benefit and confidence, risk, permission need, preview, backup, verification, history entry, and undo behavior before it can be applied. The user must confirm each change. Failure, partial completion, cancellation, permission denial, and a changed external value must remain visible as outcomes.

## Implemented scopes

### User startup entries

The app inventories Run/RunOnce registry entries from user and machine hives and startup folders. Entries from machine hives and startup folders are view-only. Current-user Run/RunOnce entries can be disabled/restored after confirmation. The original data is saved locally, the changed registry state is read back, and restoration detects conflicts rather than overwriting an unrelated later edit. Publisher text comes from file version metadata and is descriptive, not a cryptographic signature verdict. Users should verify an unfamiliar item before disabling it.

### Temporary file cleanup

The cleanup preview scans only the top level of the current user's Temp directory, selects files with `.tmp`, `.temp`, or `.cache` extension older than 30 days, and caps the scan at 5,000 files. It never traverses folders or personal document locations. The reviewed file set and scope are revalidated when execution begins; locked or changed files may be skipped and reported. This does not promise to reclaim all previewed space if files are in use.

### Windows media-aware drive maintenance

The user selects one ready local fixed volume discovered by Windows. After a separate confirmation, WinOptimizer launches the built-in `defrag.exe <volume> /O /U /V` operation using Windows' standard UAC elevation flow. Microsoft documents `/O` as selecting the appropriate optimization for each media type; the app does not classify a drive as SSD/HDD when that information is unavailable or implement a second defragmentation algorithm. The native progress/report window remains visible. The app records Windows' process completion status, not a measured performance improvement. The operation can take time and use disk activity; it has no undo because it changes on-disk layout/maintenance state rather than app configuration. Declining UAC starts no operation.

### AC-only performance power plan

The built-in High performance plan is offered only when the PowrProf API confirms the scheme is present, an active plan is identifiable, and Windows reports AC power. The action is rechecked at apply time, changes only the active user power scheme through `PowerSetActiveScheme`, and reads the active plan back before marking the setting change verified. It may increase energy use, heat, and fan noise; no speed increase is promised. History stores the original plan ID. Restore checks that the plan still matches WinOptimizer's last applied value before changing it, so external edits are not overwritten. If AC power is unknown or disconnected, the recommendation is omitted and apply is blocked.

## Explicitly excluded

- No automatic startup disabling or process termination.
- No fake “RAM cleaning,” page-file disabling, or memory compression changes.
- No Defender, Firewall, UAC, Update, or essential-service disabling.
- No arbitrary registry packs, boot options, speculative networking settings, fan writes, or disk “repair” claims.
- No machine-wide registry or service changes. The drive-maintenance action is the only feature that may request elevation, and the power-plan action is user-confirmed and uses the documented Windows power API.

## History and rollback

Local history records operation, timestamp, target, result, and restoration availability. It is capped and kept on the device. Backups are specific to user startup and power-plan changes. A backup does not guarantee restoration if configuration changes outside the application; conflicts are surfaced and require review. Cleanup and drive maintenance cannot be undone.

## Measuring results

The current release provides telemetry and history, not a validated benchmark suite or startup-time attribution. Compare measurements only when workload, power, updates, and idle conditions are similar. A configuration change alone does not prove that responsiveness improved.

