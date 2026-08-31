# SRS-XB31 Windows Control UI Design

Date: 2026-08-24

## Objective

Expand the existing, proven SRS-XB31 Power Off helper into a compact Windows control application without weakening the headless shutdown workflow. The first UI release provides Power Off and all 13 disclosed lighting modes. Sound mode, Auto Standby, and Battery/Status remain visible only as honest protocol-pending integration points.

## Constraints

- Discover an exact, case-insensitive paired `SRS-XB31` and use service UUID `B9B213CE-EEAB-49E4-8FD9-AA478ED1B26B` without persisting a device address.
- Preserve the transaction model: discover, connect, write one frame, flush, and close.
- Preserve the existing PowerShell wrapper, stable helper exit codes, helper output location, and shutdown-script behavior.
- Do not make the shutdown workflow launch or depend on the GUI.
- Keep discovery, connection, and write operations bounded by the existing 10-second, 10-second, and 5-second timeouts.
- Do not invent pending protocol commands, queried state, or battery values.
- Do not add a daemon, tray integration, installer, updater, pairing UI, telemetry, or unrelated device support.

## Architecture

Use three focused components:

1. `XB31.Core`, targeting `.NET 8` for Windows, owns the SRS-XB31 command definitions, frame construction, result types, and the proven WinRT RFCOMM transaction.
2. The existing `Xb31PowerOff` console project remains a `.NET 8` headless executable at `bin/Release/net8.0-windows10.0.26100.0/Xb31PowerOff.exe`. It references `XB31.Core` and preserves the current CLI arguments, messages, exit codes, and non-interactive behavior.
3. `XB31.Control`, targeting `.NET 10` WPF, references `XB31.Core` and provides the XAML UI. It never launches the console helper or duplicates socket code.

The root console project must explicitly include only its own source so the new projects beneath `src` are not compiled into it by SDK wildcard discovery. A solution file will group all projects, while existing direct `dotnet build Xb31PowerOff.csproj --configuration Release` usage continues working.

## Core Boundaries

`Xb31FrameBuilder` builds device-control frames from a payload. The frame layout is:

```text
3e 00 00 00 00 00 [payload length] [payload] [checksum] 3c
```

The checksum is the low byte of the sum of the payload-length byte and every payload byte. This construction is confirmed by all three known frames:

- Power Off payload `30 00 00 0f 00` produces checksum `44` and frame `3e0000000000053000000f00443c`.
- Light Off payload `f4 11 10 ff 00 00` produces checksum `1a` and frame `3e000000000006f41110ff00001a3c`.
- Chill payload `f4 11 12 ff 00 00` produces checksum `1c` and frame `3e000000000006f41112ff00001c3c`.

`LightingMode` is a typed definition with these exact values:

| Mode | Byte |
|---|---:|
| Light Off | `0x10` |
| Rave | `0x11` |
| Chill | `0x12` |
| Random Flash Off | `0x13` |
| Hot | `0x14` |
| Cool | `0x15` |
| Strobe | `0x16` |
| Calm Magenta | `0x17` |
| Calm Cyan | `0x18` |
| Calm Lime | `0x19` |
| Calm Cinnabar | `0x1A` |
| Calm Daylight | `0x1B` |
| Calm Light Bulb | `0x1C` |

Lighting uses the payload `f4 11 [mode] ff 00 00`; it does not store 13 unrelated full frames.

`IXb31Client` exposes bounded asynchronous operations for availability probing, Power Off, and Set Lighting. The concrete client keeps the existing address, UUID, pairing validation, service-device address validation, authenticated encrypted socket protection, typed `DataWriter`, exact one-shot write, flush, and guarded disposal.

Operations return a typed result that distinguishes success, unavailable/service failure, connection failure, write failure, timeout, malformed command, and unexpected failure. The console adapter maps these results back to the existing exit codes `0`, `10`, `11`, `12`, `13`, and `14` and preserves the current concise output. The UI maps them to human-readable status without displaying stack traces.

Future `SetSoundMode`, `SetAutoStandby`, and `QueryStatus` methods are not added until their protocol definitions exist. The current boundaries make adding those typed operations possible without changing transport architecture.

## UI Composition

The WPF application uses a compact, fixed-size window of approximately 440 by 560 device-independent pixels. XAML owns presentation, styles, templates, visual states, and restrained animation. A lightweight view model owns state and asynchronous actions; no external MVVM or visual framework is introduced.

The visual system uses an obsidian background, cyan and violet accents, low-intensity edge glow, rounded controls, compact typography, and a subtle connecting-state animation. It should read as a futuristic wireless-speaker remote, not a card-heavy dashboard. Standard WPF accessibility behavior and keyboard focus remain available through templated native controls.

The window contains:

- Device header: `SRS-XB31`, a small state indicator, and concise state text.
- Status line: Battery `--` and a subtle `Protocol pending` label.
- Lighting section: one ComboBox containing all 13 known modes and a last-command status line.
- Sound section: disabled Extra Bass and Standard controls with `Protocol pending`.
- Auto Standby section: a disabled On/Off control with `Protocol pending`.
- Device section: a clear, restrained Power Off button.

The Lighting ComboBox initially has no selected mode and displays `Select lighting mode`. The application therefore cannot imply a queried current mode or accidentally send the first item during XAML initialization.

## UI State and Command Flow

Window loading starts one asynchronous, no-data availability probe. It may open and close RFCOMM but never writes a control frame. The initial state progresses from `Connecting` to `Available` or `Unavailable`. `Available` means the latest bounded probe succeeded; it does not imply a persistent connection.

When the user deliberately selects a lighting mode:

1. The view model enters busy state and disables actionable controls.
2. It changes the status to `Connecting`.
3. It awaits one `SetLightingAsync` transaction without blocking the UI thread.
4. On success, it reports `Last sent: [mode]` and `Available`.
5. On failure, it reports a concise state such as `Speaker unavailable`, `Connection failed`, `Command timed out`, or `Command failed`.
6. It leaves busy state and re-enables actionable controls.

An operation gate allows at most one transaction at a time. The ComboBox initialization guard, disabled busy state, and operation gate prevent duplicate selection/change events from creating command storms. The UI only records the last successfully sent command; it never labels that value as confirmed current speaker state.

Power Off follows the same asynchronous gate and result flow and calls the shared core Power Off operation. A successful Power Off leaves a concise `Power off sent` state. The headless helper continues to use the same operation with no GUI or user interaction.

## Error Handling and Diagnostics

The shared client handles speaker absence, Bluetooth unavailability, service absence, discovery timeout, connection timeout/failure, write timeout/failure, malformed frame construction, unexpected exceptions, and cleanup failure. All WinRT resources are disposed even on error.

The view model converts typed failures to short UI messages and remains responsive. Detailed exception information is limited to diagnostic/debug output and the console helper's existing `--verbose` behavior.

## Testing

Implementation proceeds test-first.

Core tests verify:

- Every lighting label maps to its exact byte from `0x10` through `0x1C`.
- The checksum algorithm for every disclosed lighting mode.
- Exact Power Off, Light Off, and Chill frame bytes.
- Frame rejection for malformed or oversized payload input.
- One transport write per command, flush, close, cleanup after failure, and bounded cancellation behavior through injectable seams around the transaction.

View-model tests use a fake `IXb31Client` and verify:

- Construction and ComboBox initialization send no control command.
- Window initialization performs only the explicit no-data probe.
- A deliberate selection issues exactly one lighting command.
- Busy state blocks duplicate operations.
- Awaiting a delayed/failing client does not synchronously block the caller or UI dispatcher.
- Typed failures produce concise status text.
- Pending controls remain disabled and battery remains unknown.

Build and regression verification includes:

- Build the `.NET 8` core and headless helper.
- Build the `.NET 10` WPF/XAML application.
- Run new automated tests and every existing Power Off test.
- Run `Shutdown-With-Xb31.ps1 -DryRun` and confirm Power Off remains headless, non-interactive, and GUI-independent.
- Confirm the unchanged shutdown policy still continues when the XB31 helper returns a failure; never invoke a real Windows shutdown during development without explicit authorization.

After all offline verification, controlled live tests send Chill once and wait for physical confirmation, then Light Off once and wait for confirmation, followed by a small selection of other modes at a deliberate pace. No live command is sent without the user's explicit readiness confirmation.

## Completion Criteria

The work is complete when the existing shutdown automation and exact Power Off frame remain operational; the compact .NET 10 WPF application builds and stays responsive; all 13 lighting modes generate tested frames and can be sent once per deliberate selection; future features remain disabled and truthful; all automated and shutdown DryRun regressions pass; and controlled lighting tests receive physical confirmation.

The remaining protocol information required after this work is the proven command and response definitions for Sound Mode, Auto Standby, and Battery/Status.
