# Optimization safety model

WinOptimizer is a maintenance assistant, not an automatic “tweak” engine. High resource usage is not automatically a defect, and an optimization is never considered beneficial without before/after evidence under comparable conditions.

## Operation lifecycle

Every system-changing recommendation should describe its identifier/category, finding, eligibility, target, expected benefit and confidence, risk, permission need, preview, backup, verification, history entry, and undo behavior before it can be applied. The user must confirm each change. Failure, partial completion, cancellation, permission denial, and a changed external value must remain visible as outcomes.

## Implemented scopes

### User startup entries

The app inventories Run/RunOnce registry entries from user and machine hives and startup folders. Entries from machine hives and startup folders are view-only. Current-user Run/RunOnce entries can be disabled/restored after confirmation. The original data is saved locally, the changed registry state is read back, and restoration detects conflicts rather than overwriting an unrelated later edit. Publisher text comes from file version metadata and is descriptive, not a cryptographic signature verdict. Users should verify an unfamiliar item before disabling it.

### Temporary file cleanup

The cleanup preview scans only the top level of the current user's Temp directory, selects files with `.tmp`, `.temp`, or `.cache` extension older than 30 days, and caps the scan at 5,000 files. It never traverses folders or personal document locations. The reviewed file set and scope are revalidated when execution begins; locked or changed files may be skipped and reported. This does not promise to reclaim all previewed space if files are in use.

## Explicitly excluded

- No automatic startup disabling or process termination.
- No fake “RAM cleaning,” page-file disabling, or memory compression changes.
- No Defender, Firewall, UAC, Update, or essential-service disabling.
- No arbitrary registry packs, boot options, speculative networking settings, fan writes, or disk “repair” claims.
- No machine-wide changes or elevation in this initial version.

## History and rollback

Local history records operation, timestamp, target, result, and restoration availability. It is capped and kept on the device. Backups are specific to user startup changes. A backup does not guarantee restoration if an entry changes outside the application; conflicts are surfaced and require review. Cleanup deletes selected files and cannot be undone.

## Measuring results

The current release provides telemetry and history, not a validated benchmark suite or startup-time attribution. Compare measurements only when workload, power, updates, and idle conditions are similar. A configuration change alone does not prove that responsiveness improved.
