# SRS-XB31 Windows control

An unofficial Windows application for controlling a paired SRS-XB31 over Bluetooth Classic RFCOMM. It provides a compact WPF dashboard for power, lighting, Sound Mode, Auto Standby, battery status, display-light synchronization, and tray operation.

This project is independently developed and is not affiliated with, endorsed by, or supported by the device manufacturer. SRS-XB31 is a trademark of its respective owner.

## Requirements

- Windows 10 version 2004 or later
- A paired Bluetooth device named `SRS-XB31`
- A .NET SDK capable of building the .NET 8 and .NET 10 Windows targets

The application enumerates paired devices and selects an exact, case-insensitive `SRS-XB31` name that exposes the expected RFCOMM service. It does not store or log a Bluetooth address. If more than one matching speaker is paired, deterministic Windows device-ID ordering selects one.

## Build and run

```powershell
dotnet build .\SRS-XB31.slnx --configuration Release
& .\src\Xb31.Control\bin\Release\net10.0-windows10.0.26100.0\Xb31.Control.exe
```

At startup, the dashboard opens an initialized RFCOMM session and reads Lighting, Sound Mode, Auto Standby, battery, and connection status. Controls send one bounded command and read back values where the speaker protocol supports confirmation.

The **Start with Windows** option writes a user-scoped `HKCU` Run value. Startup mode initializes the tray icon and monitoring without opening the dashboard. Only one application instance runs; launching it again restores the existing window.

## Display synchronization

The optional **Sync lighting with display** setting mirrors effective display ON/OFF state to XB31 lighting. DIM is diagnostic-only and does not alter the effective synchronization state. If a display transition arrives while another speaker operation is active, only the latest desired automatic lighting state is retained and applied afterward.

## Headless power control

Build Release before using the wrapper:

```powershell
# Send the speaker power-off command
& .\PowerOff-Xb31.ps1

# Discover and connect without sending application data
& .\PowerOff-Xb31.ps1 -Probe

# Print the generated power-off frame without Bluetooth access
& .\PowerOff-Xb31.ps1 -VerifyFrame
```

`-VerboseDiagnostics` may be added for transport troubleshooting.

## Combined Windows shutdown

`Shutdown-With-Xb31.ps1` powers off the speaker and then invokes a forced Windows shutdown. Forced shutdown can discard unsaved work, so a real shutdown requires explicit consent:

```powershell
& .\Shutdown-With-Xb31.ps1 -ForceShutdown
```

The safe verification mode does not require consent, sends no power-off command, and never invokes Windows shutdown:

```powershell
& .\Shutdown-With-Xb31.ps1 -DryRun
```

The tray application also handles ordinary Windows shutdown. During `WM_QUERYENDSESSION` it registers the visible reason `Turning off SRS-XB31...` and performs one bounded power-off transaction before allowing WPF shutdown to continue. Successful delivery releases immediately; unsuccessful delivery holds shutdown only until the 20-second absolute cap. `WM_ENDSESSION` is diagnostic only. Logoff, Restart Manager `CLOSEAPP`, and critical shutdown do not trigger the transaction.

Shutdown actions are appended to `%LOCALAPPDATA%\XB31 Control\shutdown.log`.

## Repository layout

```text
src/Xb31.Core/        Tandem protocol, RFCOMM transport, IXb31Client
src/Xb31.Control/     WPF dashboard, tray, display sync, shutdown hook
src/Xb31.PowerOff/    headless CLI helper, built as Xb31PowerOff.exe
tests/Xb31.*.Tests/   MSTest unit tests
tests/*.Tests.ps1     PowerShell integration and contract tests
PowerOff-Xb31.ps1     operator entry point for speaker power-off
Shutdown-With-Xb31.ps1   explicit combined speaker/Windows shutdown
```

## Protocol behavior

Each operation uses a bounded RFCOMM session. Tandem frames use escaped delimiters, declared payload length, alternating sequence bits where required, and an additive checksum. Incoming data frames are acknowledged; unrelated valid responses are consumed until the expected response arrives. Requests and setting commands are not retried automatically.

Party Booster, volume, Bluetooth Standby, Bluetooth Codec, voice battery level, unsupported battery families, and MC1 commands are outside this implementation.

## Verification

Run the complete offline gate from Windows PowerShell:

```powershell
& .\tests\Run-All.ps1
```

The test gate builds Release, runs both MSTest projects, and executes every PowerShell contract. Shutdown coverage uses isolated fixtures or `-DryRun`; it never shuts down Windows or contacts the speaker.

## License

Copyright © 2026 ryancooper73.

This project is free software licensed under the GNU General Public License, version 3 or any later version (`GPL-3.0-or-later`). See [LICENSE](LICENSE). It is provided without warranty.
