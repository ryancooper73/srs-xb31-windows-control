# XB31 Bidirectional Controls Design

## Scope

Extend the existing Windows SRS-XB31 application with the recovered legacy Tandem protocol features required by the current dashboard:

- complete Tandem frame encoding and decoding;
- bounded request/response RFCOMM transactions with ACK handling;
- battery status-label read;
- Sound Mode read and write for Standard, Extra Bass, and Live Sound;
- Auto Standby read and write;
- readable closed and open ComboBox colors;
- preservation of the proven Power Off helper, shutdown automation, and lighting controls.

Party Booster is explicitly excluded. Volume, Bluetooth Standby, Bluetooth Codec, BATT Level (Voice), the unsupported `66/67/68` battery family, and MC1 commands are also outside this change.

## Chosen approach

Build a small bidirectional Tandem codec and request path in `Xb31.Core`, while retaining fresh bounded RFCOMM sessions for every operation. This is preferred over two alternatives:

1. Hardcode only the new setter frames. This would be smaller initially, but could not report real battery or current setting state and would leave escaping bugs latent.
2. Keep a persistent RFCOMM session. This would reduce reconnects, but adds lifecycle, reconnection, background ownership, and sequence-state complexity that the dashboard does not need.

Fresh sessions preserve the working sequence-zero behavior. Each operation sends exactly one application request or setting command; protocol ACKs do not count as application commands.

## Tandem framing

The shared codec represents a decoded frame as frame type, sequence byte, and application payload.

Encoding performs these steps:

1. Construct `type | sequence | payload_length_be32 | payload`.
2. Compute the additive low-byte checksum over those unescaped bytes.
3. Append the checksum.
4. Byte-stuff every inner `3C`, `3D`, and `3E` byte as `3D 2C`, `3D 2D`, and `3D 2E` respectively.
5. Wrap the escaped inner bytes in `3E` and `3C` delimiters.

Decoding reverses byte-stuffing, validates the four-byte length, checksum, delimiters, frame type, and escape sequences, then returns a typed decoded frame. Malformed input is rejected without exposing partial payloads.

The existing public Power Off and Lighting frame factories remain compatible and must continue producing their exact proven bytes. New tests cover reserved bytes, a reserved checksum, sequence one, malformed escapes, incorrect length, and incorrect checksum.

## RFCOMM transactions

`IRfcommSession` gains bounded chunk reading in addition to its existing single-write operation. Windows uses a `DataReader` over `StreamSocket.InputStream`; cancellation and timeouts follow the existing typed error mapping.

Two transport transaction forms are supported:

- **Send:** connect, write one sequence-zero data frame, retain the empirically proven one-second post-write settle period, then dispose.
- **Request:** connect, write one sequence-zero request, read and parse frames until the expected response command arrives or the bounded response timeout expires. Incoming ordinary data frames are ACKed with the same sequence. Incoming ACK frames are consumed but never returned as application responses. The session is then disposed.

Parser state accepts fragmented or coalesced socket reads. A bounded maximum frame size prevents untrusted length fields from allocating arbitrary memory.

## Typed commands and responses

The core adds these application payloads:

| Operation | Payload |
|---|---|
| Sound Mode read | `91 10 0F FF 00` |
| Sound Mode Standard | `93 10 00 FF 00 00` |
| Sound Mode Extra Bass | `93 10 01 FF 00 00` |
| Sound Mode Live Sound | `93 10 02 FF 00 00` |
| Auto Standby read | `F2 12 1F FF` |
| Auto Standby off | `F4 12 1F FF 01 01 00` |
| Auto Standby on | `F4 12 1F FF 01 01 01` |
| Battery label read | `F2 12 3F FF` |

Sound responses require command `92`, target `10`, a known selected candidate `00/01/02`, and the expected `FF` ID suffix. Auto Standby responses require command `F3`, category `12`, element `1F FF`, ON/OFF data type and a one-byte value `00/01`. Battery responses require command `F3`, category `12`, element `3F FF`, a declared data length fitting within the payload, and a non-empty decoded display label.

Public client operations use a typed query result carrying status, optional value, and diagnostic. Existing `Xb31Result` and CLI exit behavior remain unchanged.

Sound Mode and Auto Standby writes are followed by a fresh read-back transaction before the UI presents the new value as confirmed. A successful write with failed read-back is reported as sent but unconfirmed.

## Dashboard behavior

Startup performs the three read operations sequentially in separate fresh sessions. It does not send setting changes. Each section owns its state independently, so one unavailable value does not erase other successful results or prevent Power Off and Lighting use.

- Battery displays the returned localized label, such as `Fully charged`; it does not invent a percentage.
- Sound exposes Standard, Extra Bass, and Live Sound. Programmatic initialization must not trigger a write.
- Auto Standby exposes On and Off. Programmatic initialization must not trigger a write.
- Lighting retains its existing last-command semantics because Lighting Mode read is not part of this scope.

The ComboBox style explicitly sets a high-contrast foreground and dark background for both the closed selection presenter and dropdown items. Disabled states remain readable.

All operations retain the existing single-operation gate. Status text distinguishes connecting, reading, command sent, confirmed state, timeout, malformed response, and unavailable speaker without showing protocol dumps in the normal UI.

## Safety and validation

- No application setting command is retried automatically.
- Every operation has bounded discovery, connection, write, and response timeouts.
- Power Off and Lighting remain exactly one application command per user action.
- Startup only performs reads.
- No Party Booster code or UI is added.
- Offline tests cover exact payloads, framing/escaping/parser behavior, fragmented reads, ACK behavior, typed response parsing, view-model initialization, no programmatic write, write/read-back outcomes, ComboBox colors, CLI compatibility, and shutdown DryRun behavior.
- Live validation is performed only after offline verification and explicit user authorization: reads first, then one reversible Sound Mode write and one reversible Auto Standby write.
