# Reliable XB31 shutdown design

## Status

Approved architecture for implementation planning. This document replaces the current
query-time Bluetooth shutdown transaction while preserving automatic XB31 power-off as a
hard requirement whenever Bluetooth is enabled and the XB31 is connected.

## Problem

`SystemShutdownPowerOff` currently performs RFCOMM discovery, connection, write, a
one-second command-settle delay, and disposal synchronously while handling
`WM_QUERYENDSESSION`. It can also hold the UI thread for up to 20 seconds after a failed
attempt. This makes the Windows shutdown UI wait and leaves an active Bluetooth operation
coupled to the shutdown lifecycle.

Windows requires an application to respond to `WM_QUERYENDSESSION` promptly and defer
work until later in the shutdown lifecycle. The application must not start a Bluetooth
operation after Windows has accepted shutdown because Bluetooth device power-down may
then be in progress.

## Goals

- When Bluetooth is enabled and the XB31 is connected, deliver its power-off command
  before Windows is permitted to proceed with shutdown or restart.
- Do not perform Bluetooth discovery, connect, read, write, wait, disposal, or blocking
  logging in a Windows shutdown message handler.
- Use Windows' shutdown-blocking surface as the sole in-shutdown user feedback channel;
  tray icons, toast notifications, and the dashboard are not assumed available.
- Never guess whether a user originally requested shutdown or restart.
- Leave no Bluetooth work running once Windows has accepted the final shutdown request.
- Make state transitions deterministic, unit-testable, and observable.

## Non-goals

- Proving that the physical speaker has powered off. Success means the power-off frame
  was written through a verified live RFCOMM session and given its bounded settle time.
- Overriding a critical shutdown, logoff, Restart Manager close request, a forced
  termination, process crash, or Bluetooth-driver failure.
- Automatically reconnecting Bluetooth during a shutdown request.
- Automatically reissuing an ordinary Windows shutdown or restart whose original intent
  cannot be determined from `WM_QUERYENDSESSION`.

## Architecture

### Persistent RFCOMM session

Introduce a persistent session owned by the application rather than opening a transport
session only during each operation.

`PersistentRfcommSession` owns exactly one set of `BluetoothDevice`,
`RfcommDeviceService`, service-device, `StreamSocket`, reader, and writer resources. It
serializes operations and exposes a bounded `SendPowerOffAsync` operation. It may only
be created, reconnected, or disposed by the session supervisor.

`BluetoothPowerSessionSupervisor` is the single owner of session lifecycle. Its states
are:

| State | Meaning | Shutdown decision |
| --- | --- | --- |
| `Live` | An open, idle RFCOMM session is owned by the supervisor. | Cancel the first request and send power-off through this session. |
| `Busy` | A normal speaker operation owns the session. | Cancel the request; wait only for that operation to resolve under the coordinator's deadline. |
| `Disconnected` | No live session and Windows reports no connected XB31. | Permit shutdown immediately. |
| `Unavailable` | Bluetooth is disabled or the target is absent. | Permit shutdown immediately. |
| `Faulted` | Bluetooth is enabled and XB31 is reported connected, but no usable session exists. | Cancel shutdown; do not reconnect during shutdown. |
| `Closing` | Session resources are being released after a delivery attempt. | Keep shutdown cancelled until cleanup completes or fails. |

The supervisor may establish or repair a session during ordinary application operation.
Once shutdown handling begins, its reconnect gate closes. A stale `Live` session is not
treated as delivered until its final write succeeds; a failed final write transitions to
`Faulted`.

All UI operations that use the speaker must be routed through the supervisor so they
cannot overlap with a final power-off transaction.

### Shutdown coordinator

`ShutdownCoordinator` is a thread-safe state machine that is independent of WPF:

```text
Idle
  -> CancellingForDelivery
  -> Delivering
  -> ClosingSession
  -> DeliveryLease
  -> Idle

CancellingForDelivery / Delivering / ClosingSession
  -> PausedFailure
  -> Idle only after the user starts a later shutdown attempt or normal-session recovery
```

Only one coordinator operation may be active. Duplicate `WM_QUERYENDSESSION` and WPF
notifications share the same operation and do not issue a second frame.

The delivery operation has a small independent deadline. It may write the prebuilt
power-off frame, wait for the protocol's required bounded settle interval, and close the
persistent session. It must not enumerate devices, open a service, reconnect, retry the
write, or block a window-message thread.

Successful delivery creates a memory-only, single-use `DeliveryLease` with a short
expiry (initially 60 seconds). It exists so the user's immediate retry of their chosen
shutdown or restart is allowed without another Bluetooth operation. The lease is cleared
when consumed, on expiry, on normal app exit, or if a new connection is established. It
is never persisted across processes or sessions.

### Windows shutdown gate

`WindowsShutdownGate` replaces the current synchronous work in
`SystemShutdownPowerOff`.

On a normal `WM_QUERYENDSESSION`:

1. If an unexpired `DeliveryLease` exists, consume it and return `TRUE` immediately.
2. If the supervisor reports `Unavailable` or `Disconnected`, return `TRUE` immediately.
3. Otherwise, call `ShutdownBlockReasonCreate` (or retain an existing reason), queue
   the coordinator, and return `FALSE` immediately.

Returning `FALSE` intentionally cancels the first Windows shutdown request. The computer
and Bluetooth stack remain fully active while the worker delivers the speaker command.
The registered reason, initially `Turning off SRS-XB31...`, is the only feedback that may
be relied on during this phase. It remains registered while the coordinator is delivering
or paused in failure, and is destroyed after successful session cleanup or application
exit.

The user then retries their original Windows action. The delivery lease permits that
second request. The application does not call `shutdown.exe`, `ExitWindowsEx`, or another
shutdown API for ordinary user-initiated requests, because the window message does not
reliably identify restart versus shutdown.

Critical shutdown, logoff, and `ENDSESSION_CLOSEAPP` continue immediately without
speaker I/O. `WM_ENDSESSION` is diagnostic-only. WPF `Application.SessionEnding` is also
diagnostic-only and may not start a late power-off fallback.

### Explicit app-controlled shutdown

An optional explicit tray command may later offer a combined "Shut down Windows and
SRS-XB31" workflow. It invokes the coordinator before it initiates Windows shutdown, so
the requested Windows action is known. This is separate from the ordinary system
shutdown gate and is not required for the initial migration.

## User-visible behavior

| Situation | Windows shutdown behavior | Bluetooth behavior |
| --- | --- | --- |
| XB31 connected, live session | First request is cancelled and Windows displays the registered reason. | One final frame is sent through the existing session. |
| Delivery succeeded | User retries their same action; it proceeds immediately once. | Session was closed before retry. |
| Session stale, final write fails | Shutdown remains cancelled; block reason stays registered. | No reconnect or retry occurs in shutdown mode. |
| XB31 disconnected or Bluetooth disabled | Shutdown continues. | No Bluetooth work is started. |
| XB31 appears connected but no live session | Shutdown is cancelled. | The normal app can recover the session only outside the shutdown request. |
| Critical/logoff/Restart Manager request | Shutdown/logoff continues. | No speaker action is attempted. |

## Diagnostics

Keep the current append-only per-user shutdown trace but add correlation and state data:

- a monotonically generated shutdown-attempt ID;
- gate decision, supervisor state, and whether the decision consumed a lease;
- session creation, loss, final-write start/end, cleanup start/end, and bounded deadline
  result;
- `ShutdownBlockReasonCreate` and `ShutdownBlockReasonDestroy` results;
- no device address, pairing key, or frame payload.

The trace must be asynchronous or best-effort from the message-handler perspective.
Logging cannot delay its return.

## Tests

### Automated

- State-machine tests for every coordinator transition, duplicate notification, lease
  consumption, lease expiry, failure, and cancellation path.
- A gate contract test proving `WM_QUERYENDSESSION` neither waits nor calls Bluetooth
  transport code directly.
- Supervisor tests for exclusive session ownership, serialized operation, stale-session
  failure, close-on-success, and closed reconnect gate during shutdown.
- Regression tests for critical, logoff, and Restart Manager classifications.
- Existing protocol and transport tests remain unchanged; add an adapter test for
  persistent session cleanup ordering.

### Canary rollout

1. Maintain and trace the persistent session for several days without intercepting
   shutdown.
2. Exercise a manual test-only power-off command through that session; verify speaker
   operation, auto-standby behavior, reconnection, and cleanup.
3. Enable the shutdown gate behind an opt-in setting and review traces after normal
   shutdown, restart, unavailable speaker, lost session, and immediate retry cases.
4. Make it the default only after the test period has no app-associated unexpected
   shutdown (`Kernel-Power` 41 / `EventLog` 6008) or Bluetooth driver failures.

## Acceptance criteria

- A query handler returns without waiting for Bluetooth work.
- No final shutdown power-off path starts discovery or `StreamSocket.ConnectAsync`.
- A connected XB31 with a usable session receives exactly one power-off write before a
  subsequent Windows shutdown request is permitted.
- A failed or unavailable required session does not allow an ordinary shutdown to
  continue silently.
- Normal shutdown after a successful delivery uses no active app Bluetooth operation.
- The canary period demonstrates clean Windows shutdown events and no recurrence of the
  observed Bluetooth recovery condition.
