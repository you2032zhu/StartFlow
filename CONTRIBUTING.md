# Contributing

Use Windows 11 and the SDK in global.json. Read docs/ARCHITECTURE.md and run `scripts/Build.ps1 -WindowsIntegration` before sending a change. Run `scripts/Publish.ps1` for packaging changes, then follow the manual validation checklist.

Keep UI strings in the bilingual resource file, Windows-only APIs in StartFlow.Windows, and deterministic scheduling rules in Core. Never execute imported content while parsing or previewing it. Keep cancellation checks immediately before launch submission. Avoid adding network activity, telemetry or automatic startup enablement.

Use a focused branch, describe the user-visible problem and final behavior, and list the checks actually run. Report uncertain platform behavior honestly. Do not include personal paths, credentials, machine-specific configuration or unrelated binaries. Contributions are made under GPL-3.0-only; do not add incompatible license restrictions.
