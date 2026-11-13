# Display-Sync Reliability Design

## Intent

Make automatic lighting synchronization recover from one transient Bluetooth failure, leave a useful local trace for later diagnosis, and ensure Startup and the desktop launcher use a published per-user copy rather than a build-output directory.

## Goals

- Retry an automatic display-driven lighting command once when its result is `Unavailable`, `ConnectionFailed`, or `Timeout`.
- Retry only the latest still-relevant ON/OFF target after a short delay.
- Keep DIM diagnostic-only, preserve existing single pending-target coalescing, and never retry manual controls.
- Record application startup and automatic display-sync decisions/results in a best-effort user-scoped log.
- Publish a per-user release, point Start with Windows and a new desktop shortcut to it, and retire the verified obsolete shutdown PowerShell shortcut.

## Non-goals

- No general command queue, repeated retry loop, automatic Bluetooth-driver remediation, or shutdown-time Bluetooth activity.
- No change to the existing user preference semantics or manual shutdown launcher.

## Approaches considered

1. Add a general retry queue in the view model. This would mix manual and automatic work and broaden the app's behavior beyond the reported fault.
2. Treat a failed operation as a new display transition. This loses the desired target because the effective display state has already been updated.
3. Give automatic synchronization the completed `Xb31Result` and schedule one guarded retry. Chosen: it is limited to the problematic automatic path and can be cancelled logically when the display state, preference, or disposal changes.

## Design

`IXb31OperationHost` will return the actual `Task<Xb31Result>` for a started lighting operation. `MainViewModel` will preserve its existing status/UI behavior while returning the result to the caller. `DisplayLightingSync` will associate each automatic operation with a generation and retry count. A retry is scheduled only once for the three transient statuses, after a three-second delay, and starts only if the sync is still enabled, the desired effective display state has not changed, and no newer operation has superseded it.

`AppTrace` will append timestamped, non-sensitive events to `%LOCALAPPDATA%\XB31 Control\activity.log`; logging errors are ignored. App startup, display states, automatic command start/results, and retry decisions are traced.

`Install-Xb31Control.ps1` will publish the app to a new versioned folder under `%LOCALAPPDATA%\XB31 Control\releases`, replace the user Start with Windows registration and create `XB31 Control.lnk` on the desktop pointing at that published executable. It does not overwrite a running executable or delete release folders. It removes the old desktop PowerShell shortcut only after confirming its target is the obsolete `Shutdown-With-Xb31.ps1` without the supported shutdown argument.

## Constraints

- .NET 10 / existing WPF project; no additional dependencies.
- Per-user settings and installation only.
- Best-effort diagnostics must never affect speaker control.
- Existing automatic DIM and coalescing behavior remains covered by tests.
