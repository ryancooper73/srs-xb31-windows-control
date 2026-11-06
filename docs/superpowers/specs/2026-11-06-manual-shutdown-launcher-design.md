# Manual Shutdown Launcher Design

## Intent

Remove automatic Bluetooth activity from the Windows shutdown path. Provide a manually invoked Desktop batch launcher that powers off the XB31 first and only then starts Windows shutdown. This avoids using the Bluetooth stack while Windows is ending the user session.

## Goals

- The control app must no longer register, handle, or delay Windows shutdown for speaker power-off.
- A Desktop `.bat` launcher must run the existing standalone power-off path before calling `shutdown.exe`.
- On a successful speaker power-off, wait briefly for the command to settle, then shut down Windows.
- On any speaker/helper failure, show a visible Windows notification, return a non-zero exit code, and leave Windows running.
- Keep ordinary in-app and tray Power Off actions unchanged.

## Non-goals

- Diagnose, reinstall, or otherwise modify the Windows Bluetooth driver.
- Retry failed Bluetooth commands or force shutdown after a failure.
- Change the XB31 protocol, UI controls, display sync, startup registration, or single-instance behavior.

## Approaches considered

1. Retain and harden the automatic shutdown coordinator. Rejected: the observed `COMException` happened during shutdown and this would continue to issue Bluetooth I/O during the most fragile lifecycle stage.
2. Disable the automatic behavior but retain the shutdown implementation. Rejected: leaves unsupported lifecycle code in the product.
3. Remove automatic shutdown handling and provide a manual launcher. **Selected:** it moves Bluetooth work before shutdown, gives the user a clear failure result, and isolates a driver issue from Windows session teardown.

## Design

`App.xaml.cs`, `TrayApplicationController`, and `DisplayStateMonitor` will stop constructing or forwarding to `SystemShutdownPowerOff`; the shutdown handler and its trace are removed with their dedicated tests. This makes normal Windows shutdown a normal WPF application exit with no Bluetooth transaction.

`Shutdown-With-Xb31.ps1` remains the single PowerShell implementation of the manual transaction. Its current helper invocation remains the speaker command source. Its failure branch changes from “continue despite XB31 failure” to a user-visible failure notification and exit without invoking `shutdown.exe`. Its success branch keeps the short post-command wait before starting shutdown.

A committed `Shutdown-With-XB31.bat` launcher invokes the PowerShell script with `-ForceShutdown`. Installation/build packaging will place a copy on the current user’s Desktop, with a stable user-facing name. The batch file will preserve and return the PowerShell exit code so a failure is observable to callers.

## Acceptance checks

- Unit tests prove app startup, display monitoring, and tray operation do not construct or invoke a shutdown-power-off handler.
- Script tests prove a failed helper exits non-zero, creates a visible notification, and never invokes `shutdown.exe`.
- Script tests prove a successful helper waits and invokes `shutdown.exe` once.
- The launcher test proves it passes `-ForceShutdown` and propagates the result.
- Build and the existing .NET and PowerShell test suites pass.
