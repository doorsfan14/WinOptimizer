# Security policy

## Supported versions

The project is under active development. Until the first tagged release, report security issues against the current default branch. After release, the latest tagged version is the supported version unless a security notice says otherwise.

## Reporting a vulnerability

Please use GitHub's **Report a vulnerability** feature for this repository when available. If private reporting is not enabled, contact the repository owner through the GitHub profile and ask for a private reporting channel. Do not publish exploit details, personal diagnostics, registry exports, crash dumps, or secrets in a public issue.

Include the affected version/commit, Windows edition/build, impact, and minimal reproduction steps. Do not attach machine-specific logs until they have been reviewed for usernames, paths, device IDs, and other personal information.

## Security design

- The app runs as the current user (`asInvoker`); it does not bypass Windows permission boundaries or self-elevate.
- Telemetry is local. There are no analytics, automatic uploads, remote command execution, downloaded scripts, or hidden startup services.
- System mutations require a user action, a scope-specific preview, and a confirmation. Machine-level startup entries are view-only.
- Local settings, history, backups, and logs are stored under the user's Local Application Data. Logs can contain exception text and paths; review before sharing.
- Dependencies and GitHub Actions should be kept current and reviewed. Release binaries are unsigned unless a maintainer configures a signing certificate.

## Safe handling

Treat diagnostics and screenshots as potentially personal. Do not ask users to disable Defender, Firewall, UAC, Windows Update, or other security controls to troubleshoot WinOptimizer.
