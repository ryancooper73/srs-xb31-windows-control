# XB31 Tray and Display Lighting Sync Design

## Scope

Extend the existing .NET 10 WPF XB31 controller so it remains available in the Windows notification area and reacts to display-power transitions:

- display OFF sends `LightingMode.LightOff`;
- display ON sends `LightingMode.Chill`;
- display DIM is diagnostic-only, sends nothing, and does not change the effective ON/OFF synchronization state;
- repeated notifications for the same state send nothing.

The existing detailed window, startup status read, Bluetooth core, lighting/sound/standby controls, Power Off behavior, CLI helper, and shutdown scripts remain intact. The shutdown path must continue to work when the tray application is not running.

## Current architecture

`Xb31.Control` is a WPF executable. `App.OnStartup` creates one `IXb31Client`, constructs `MainWindow`, and shows it. `MainWindow` owns `MainViewModel`; the view model performs the existing bounded status and command calls. Closing the only window currently ends the process. There is no tray or power-notification infrastructure.

`Xb31.Core` contains the shared protocol and fresh-session RFCOMM transport. `Xb31Client.SetLightingAsync` already sends the proven lighting command with bounded discovery, connection, write, and cleanup behavior.

The separate `Xb31PowerOff` console executable and PowerShell shutdown automation reference `Xb31.Core` directly. They do not launch or communicate with `Xb31.Control`.

## Chosen approach

The WPF `App` owns a small application-lifetime coordinator: one main window, one notification-area icon, and one display-state registration. This keeps process-lifetime responsibilities out of the existing view model and leaves the shared core and headless shutdown path unchanged.

Two alternatives are rejected:

1. Put tray ownership and native power handling directly in `MainWindow`. This saves a class but couples background process lifetime to a window that is intentionally hidden and reopened.
2. Create a separate hidden native message window. This isolates native messages but adds another window lifetime when the existing WPF `HwndSource` can receive the supported message cleanly.

The control project enables Windows Forms only to use the built-in `NotifyIcon` and menu types. No third-party tray dependency is added.

## Application and window lifecycle

`App` uses explicit shutdown mode so the process remains alive when the main window is hidden. It creates one `MainWindow` and routes tray and display actions through that window's existing `MainViewModel`. This reuses the current non-blocking single-operation gate and keeps status updates consistent without adding a second command layer.

The main window intercepts a normal close request and hides. The tray Open action restores, activates, and foregrounds the same window; it never creates a second dashboard. The existing custom X button follows the same hide path.

Tray Exit sets an application-exit flag, unregisters display notifications, removes the WPF message hook, disposes the tray icon/menu, closes the window without interception, and shuts down the application. Cleanup is idempotent so normal application teardown cannot leave an icon or registration behind.

## Display-state notification

After the WPF window handle exists, a `DisplayStateMonitor` adds an `HwndSource` hook and calls `RegisterPowerSettingNotification` for `GUID_SESSION_DISPLAY_STATUS` (`2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5`) with `DEVICE_NOTIFY_WINDOW_HANDLE`.

The hook accepts only `WM_POWERBROADCAST` messages whose `wParam` is `PBT_POWERSETTINGCHANGE`. It marshals the `POWERBROADCAST_SETTING` header, verifies the registered GUID and a four-byte data value, then maps `0`, `1`, and `2` to Off, On, and Dim. Unknown or malformed values are ignored. Disposal calls `UnregisterPowerSettingNotification` and removes the hook.

During the diagnostic checkpoint, each accepted value writes a minimal debug line such as `Display state -> OFF` and appends it to a temporary, unobtrusive diagnostic line in the existing window. This lets the user see the OFF/ON sequence after waking without installing a debug viewer. The initial status notification, if supplied by Windows at registration time, establishes the last-known state and never sends a Bluetooth command. Diagnostics must show a reliable initial/baseline state and subsequent transitions before automation is enabled. If the window-message registration does not establish a usable baseline on this machine, implementation will switch only the notification wrapper to the supported callback registration API that supplies the current value immediately; no polling or Twinkle Tray integration will be introduced. The temporary in-window diagnostic line is removed after validation.

## Display synchronization

A small `DisplayLightingSync` component receives parsed display states and owns the last effective On/Off state. It is enabled by default when no saved preference exists.

- The first observed On or Off state establishes the baseline and sends nothing; an earlier Dim notification remains diagnostic-only.
- A later genuine transition to Off sends `LightOff` once.
- A later genuine transition to On sends `Chill` once.
- A Dim notification is reported diagnostically but does not update the effective On/Off state or send anything. Therefore On -> Dim -> On and Off -> Dim -> Off send no commands.
- Repeating the current state sends nothing.
- While synchronization is disabled, states are observed but no commands are sent. Re-enabling establishes the current effective On/Off state as the new baseline and does not immediately change the speaker.

Only display transitions invoke synchronization. Manual lighting choices are never re-enforced while the display remains on. Thus a manual Rave selection remains until a later OFF transition, followed by Chill on a later ON transition.

Each automatic transition invokes the existing view-model command asynchronously after the window-message callback returns, so native message handling is never blocked. If any XB31 operation is already active, the synchronizer stores at most one pending desired automatic lighting mode. Further effective OFF/ON transitions replace that pending value with the latest desired mode. DIM never changes it. When the active operation finishes, the latest pending value is attempted once; if it matches the automatic mode already in flight, it is cleared instead of resent. This is a single coalesced slot, not a general queue or retry system.

A failed or unavailable-speaker automatic attempt uses the existing concise window status and debug diagnostics and is not retried. Only a later genuine effective OFF/ON transition can create another desired state. Disabling synchronization clears any pending automatic state; it does not cancel a command already sent.

## Tray and minimal UI

The tray icon reuses `Assets/Xb31Control.ico`. Its menu contains only:

1. Open XB31 Control
2. Sync lighting with display (checked state)
3. Light Off
4. Chill
5. Power Off
6. Exit

Double-clicking the icon also opens the existing window. Manual tray commands use the same single-operation view-model path as the window and make at most one bounded attempt.

The existing window receives two compact checkboxes without redesign:

- Sync lighting with display
- Start XB31 Control with Windows

The sync checkbox and tray check item share one setting persisted as a per-user DWORD under `HKCU\Software\SRS-XB31`. A missing value defaults to enabled. Start-with-Windows reflects and updates a per-user registry value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointing to the current executable path. Both settings require no elevation, installer, service, or helper process.

## Validation sequence

Implementation follows the required gates:

1. Preserve the already-passing pre-change Release build, 127 core tests, 30 control tests, and PowerShell/DryRun suite.
2. Add only the native display monitor and minimal diagnostics, launch the application, and obtain manual confirmation that Twinkle Tray sleep/wake and normal Windows timeout/wake produce OFF/ON events.
3. Only after that confirmation, enable `DisplayLightingSync` and tray/application lifecycle behavior.
4. Unit-test first-observation behavior, OFF/ON mapping, DIM preserving the effective state, duplicate suppression, latest-state coalescing during an active operation, disabled synchronization, unavailable-speaker completion without retry, and command serialization with a fake client.
5. Test tray/window lifecycle, synchronization-preference persistence/defaulting, and startup-registry behavior at the smallest practical seam; retain existing markup/startup contract tests.
6. With the speaker ready, manually confirm OFF turns its lighting off, wake restores Chill, manual overrides remain until the next transition, and an unavailable speaker does not freeze or retry.
7. Run the full Release suite and `Shutdown-With-Xb31.ps1 -DryRun`. Do not perform an actual Windows shutdown.

## Explicit exclusions

No continuous polling, Twinkle Tray process integration, inactivity inference, persistent Bluetooth session, reconnect daemon, Windows service, IPC, installer, logging framework, notification spam, generalized automation, additional device commands, dependency upgrade, or unrelated refactor is included.
