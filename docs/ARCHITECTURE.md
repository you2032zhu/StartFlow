# Architecture and maintenance decisions

The app uses .NET 10 LTS / WPF, with no application framework dependency. MainViewModel owns observable presentation state and commands. XAML and Dialogs are the view layer; native drag/drop and window lifecycle remain view events. Core has no WPF or Windows API dependency. Windows integration is replaceable through ITargetLauncher. The console test harness fails with nonzero status and is run explicitly by CI.

## Scheduling invariants

- A queue entry is a JSON deep clone. Editing stored plans cannot change queued content.
- One pump consumes at most 20 queued plans; duplicate plan IDs are rejected while active or waiting.
- Sequential timing starts when a queued plan becomes active. Overall delay is added once; each item delay follows the previous initial submission. Script exits and retries do not shift later item scheduling.
- Timeline due = entry reference + overall delay + item offset. Manual reference is confirmation time; sign-in reference is captured at the startup entry. IPC transports that timestamp if the manager is already running. At plan activation, overdue items preserve list order; future items sort by due time and then list index.
- First submissions are awaited to preserve order. Script exit observation and retries are independent tasks. A plan finishes only after its items and retries have completed or been cancelled.
- Stop closes the generation, rejects enqueue while stopping, cancels timers and retry tasks, clears the queue and awaits the old pump. Every launch service checks cancellation immediately before Process.Start / activation. StartFlow never kills opened targets. An already-entered native UAC request cannot be safely recalled; stop awaits that call returning.
- Runtime results are in memory only; starting the next plan replaces the previous results.

## Windows boundaries

HKCU Run launches the quoted executable with `--login`. No scheduled wake/unlock triggers are registered. TokenStatistics.AuthenticationId distinguishes interactive logon sessions, surviving app restarts/sleep/unlock. A skip token is armed with the current session ID and consumed only in a different session. LastHandledSession prevents repeated login-entry handling in the same session; manual opening never calls this policy.

A named per-user kernel-object lease and current-user-only pipe coordinate one process. Manual IPC raises the manager; login IPC passes its reference time and is deduplicated by session. The separate installer guard makes the user exit the normal application before replacement/uninstall. Test instances have independent data/pipe/startup-value scopes.

Desktop apps use ProcessStartInfo with separate executable and arguments; unprivileged normal applications do not pass through cmd. Files and shortcuts use ShellExecute, folders use Explorer, websites use default URL association. Store apps use the Windows ApplicationActivationManager and actual AUMIDs discovered in AppsFolder. Store icons are optional; activation never uses WindowsApps paths.

Shortcut import reads target, arguments, working directory and icon through the native Unicode IShellLinkW/IPersistFile interfaces, records them in notes, and launches the original .lnk to preserve shell semantics. Reading does not call Resolve or execute the target. WSH-created links can lose characters unavailable in the system ANSI code page; the regression fixture therefore creates a real Unicode link and also checks legacy ASCII links. Editing advanced arguments is an explicit override; native shortcut arguments still belong to the shortcut. System shell shortcuts without a normal executable remain supported by launching the shortcut itself.

Running detection uses full image paths for desktop apps, exact AUMIDs for Store apps, or explicit user process-name opt-in. Parameterized desktop targets without an explicit identity are treated as uncertain and launched with a visible explanation. Access failures are not reported as reliable absence. Websites, scripts, commands and shell-associated files never inherit ordinary app detection.

PowerShell command content is Unicode EncodedCommand, with NoProfile and no policy bypass. Script paths use -File with Win32 argument quoting. Cmd targets use cmd's native /d /s /c conventions. Raw cmd content and batch arguments obey cmd syntax; users should use PowerShell or direct app arguments when shell metacharacters are involved. A script exit code is an observed result; desktop process lifetime is never used as a generic success test.

## Storage and imports

Configuration format 1 has stable GUIDs and centralized validation. Unknown future formats are rejected without rewrite. This is the first format, so no fictional legacy migration is applied. Future migrations must explicitly map an older version, validate the result and preserve the original before committing.

Save writes a unique sibling file, flushes to disk and atomically replaces config.json with a .bak backup. Recovery requires the user's confirmation and makes a separate damaged-original copy. Import validates at most 16 MiB, previews risk-bearing content, backs up before replacement and never invokes startup integration. Merge gives every imported plan and item a new ID and resolves plan names. Replacement preserves local login session/skip markers. Exports remove those machine/session markers and provide an editable JSON review before writing.

No secrets are separately collected. User-authored command text may itself contain secrets, so export warns users to redact it. No automatic network requests are made by the app. External targets can perform their own network operations when the user runs them.

## Release

Current-user Inno Setup installation has no auto-start checkbox and never enables startup by itself. An upgrade repairs a pre-existing startup value only when it exactly matched the old installed executable. Uninstall verifies ownership before deleting the startup value, removes only known configuration filenames, preserves unrelated/exported files and removes the data folder only if empty. No uninstall hook runs during a normal in-place upgrade.

Full signed OS support matrices, multiple monitor positioning, forced browser tabs and forced app window state are outside v1 guarantees. Re-run the manual checklist after changes to packaging, COM activation or lifecycle code.
