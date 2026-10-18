# Reliable XB31 Shutdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reliably deliver a connected XB31's power-off frame before a later Windows shutdown proceeds, without Bluetooth I/O in a shutdown message handler.

**Architecture:** A persistent RFCOMM client is owned by a serialized supervisor. A coordinator cancels the first ordinary Windows shutdown request, delivers through the existing session, and grants a one-use 60-second lease for the user's immediate retry. The Windows hook only makes this gate decision and manages the system-provided blocking reason.

**Tech Stack:** C# / .NET 8 and .NET 10 Windows targets, WPF, WinForms tray icon, Windows Runtime Bluetooth RFCOMM APIs, MSTest, Windows PowerShell.

**Spec:** [docs/superpowers/specs/2026-10-18-reliable-xb31-shutdown-design.md](../specs/2026-10-18-reliable-xb31-shutdown-design.md)

## Global Constraints

- Preserve Windows 10 version 2004 (`10.0.19041.0`) and existing net8.0/net10.0 Windows targets.
- `WM_QUERYENDSESSION` must not discover, connect, read, write, wait, sleep, dispose Bluetooth resources, or synchronously write the trace.
- After shutdown handling begins, never discover, reconnect, or retry Bluetooth.
- The `ShutdownBlockReasonCreate` text is the sole dependable in-shutdown feedback channel.
- Do not reissue an ordinary shutdown/restart or invoke `shutdown.exe` / `ExitWindowsEx`.
- Success means one frame was written through the live session and completed the bounded settle delay; it is not proof of physical speaker power-off.
- Retain existing critical, logoff, and `ENDSESSION_CLOSEAPP` exclusions.
- Never trace Bluetooth addresses, pairing keys, or frame payload bytes.
- The feature remains opt-in until the manual canary passes; while it is off, automatic shutdown power-off is disabled rather than falling back to the unsafe legacy handler.

## Review Focus

- **Stale live socket:** Task 3 fakes `WriteFailed` and proves no reconnect or lease follows.
- **Duplicate lifecycle notifications:** Task 4 proves the hook and WPF event produce one delivery only.
- **Immediate retry:** Task 3 tests one lease consumption and Task 4 proves it causes no Bluetooth call.
- **Bluetooth disabled or speaker absent:** Task 4 proves an immediate allow decision.
- **Lease expiry:** Task 3 uses an injected clock and proves expiry requires a new delivery decision.

---

## File structure

| Path | Responsibility |
| --- | --- |
| `src/Xb31.Core/Persistent/IXb31Session.cs` | Persistent client and factory contracts. |
| `src/Xb31.Core/Persistent/Xb31SessionFactory.cs` | Creates one persistent RFCOMM client. |
| `src/Xb31.Core/Persistent/Xb31SessionClient.cs` | Owns the one RFCOMM session and its disposal. |
| `src/Xb31.Core/Transport/Xb31ConnectedTransport.cs` | Executes protocol work over an existing `IRfcommSession`; never connects/disposes. |
| `src/Xb31.Control/Shutdown/BluetoothPowerSessionSupervisor.cs` | Serializes app operations and owns session/reconnect lifecycle. |
| `src/Xb31.Control/Shutdown/ShutdownCoordinator.cs` | Deduplicates final delivery and manages the one-use lease. |
| `src/Xb31.Control/SystemShutdownPowerOff.cs` | Thin Windows gate and block-reason lifecycle. |
| `src/Xb31.Control/ShutdownTrace.cs` | Correlated, asynchronous/best-effort shutdown diagnostics. |
| `tests/Xb31.Core.Tests/PersistentXb31SessionTests.cs` | Persistent-session transport/lifecycle coverage. |
| `tests/Xb31.Control.Tests/BluetoothPowerSessionSupervisorTests.cs` | Session ownership, serialization, and reconnect-gate coverage. |
| `tests/Xb31.Control.Tests/ShutdownCoordinatorTests.cs` | Coordinator state and lease coverage. |

### Task 1: Add an already-connected core transport and persistent client

**Files:**
- Create: `src/Xb31.Core/Persistent/IXb31Session.cs`
- Create: `src/Xb31.Core/Persistent/Xb31SessionFactory.cs`
- Create: `src/Xb31.Core/Persistent/Xb31SessionClient.cs`
- Create: `src/Xb31.Core/Transport/Xb31ConnectedTransport.cs`
- Modify: `src/Xb31.Core/Xb31Client.cs`, `src/Xb31.Core/Transport/Xb31Transport.cs`, `src/Xb31.Core/Properties/AssemblyInfo.cs`
- Test: `tests/Xb31.Core.Tests/PersistentXb31SessionTests.cs`, `tests/Xb31.Core.Tests/TransportTests.cs`

**Interfaces:**
- Consumes: `IRfcommPlatform.ConnectAsync(CancellationToken)`, `IRfcommSession`, `IXb31Client`, and current protocol/transport types.
- Produces:
  - `public interface IXb31Session : IXb31Client, IAsyncDisposable { bool IsUsable { get; } }`
  - `public interface IXb31SessionFactory { Task<IXb31Session> ConnectAsync(CancellationToken cancellationToken = default); }`
  - `public sealed class Xb31SessionFactory : IXb31SessionFactory`
  - `internal sealed class Xb31ConnectedTransport`, constructed with an `IRfcommSession`.

- [ ] **Step 1: Write the failing core-session tests**

```csharp
[TestMethod]
public async Task SessionFactory_ConnectsOnceAndReusesTheConnectedTransport()
{
    var platform = new FakeRfcommPlatform(new FakeRfcommSession());
    IXb31Session session = await new Xb31SessionFactory(platform).ConnectAsync();

    await session.PowerOffAsync();
    await session.PowerOffAsync();

    Assert.AreEqual(1, platform.ConnectCalls);
}
```

Also add a test proving `DisposeAsync` disposes the underlying session once, and a `WriteFailed` test proving `IsUsable` becomes false.

- [ ] **Step 2: Run the new test class and confirm it fails**

Run: `dotnet test .\tests\Xb31.Core.Tests\Xb31.Core.Tests.csproj --filter FullyQualifiedName~PersistentXb31SessionTests`

Expected: FAIL because the persistent contracts do not exist.

- [ ] **Step 3: Implement the persistent seam**

Add the three persistent-client files. Extract the send/request work that assumes an already-open session into `Xb31ConnectedTransport`; it must not call `IRfcommPlatform.ConnectAsync` or dispose the supplied session. Keep `Xb31Transport` as the one-shot transport used by the CLI. `Xb31SessionClient.DisposeAsync` is the sole owner of resource disposal.

- [ ] **Step 4: Run focused core verification**

Run: `dotnet test .\tests\Xb31.Core.Tests\Xb31.Core.Tests.csproj --configuration Release`

Expected: PASS, including current frame and transport tests.

- [ ] **Step 5: Commit**

```powershell
git add src/Xb31.Core tests/Xb31.Core.Tests
git commit -m "feat: add persistent XB31 RFCOMM session"
```

### Task 2: Implement the serialized session supervisor

**Files:**
- Create: `src/Xb31.Control/Shutdown/BluetoothPowerSessionState.cs`
- Create: `src/Xb31.Control/Shutdown/IBluetoothPowerSessionSupervisor.cs`
- Create: `src/Xb31.Control/Shutdown/BluetoothPowerSessionSupervisor.cs`
- Modify: `src/Xb31.Control/OfflineStartupClient.cs`
- Test: `tests/Xb31.Control.Tests/BluetoothPowerSessionSupervisorTests.cs`

**Interfaces:**
- Consumes: `IXb31SessionFactory`, `IXb31Session`, `IXb31Client`, `Xb31Result`.
- Produces:
  - `internal enum BluetoothPowerSessionState { Live, Busy, Disconnected, Unavailable, Faulted, Closing }`
  - `internal interface IBluetoothPowerSessionSupervisor : IXb31Client, IAsyncDisposable` with `State`, `CloseReconnectGate()`, and `Task<Xb31Result> DeliverPowerOffAndCloseAsync(CancellationToken)`.
  - `internal sealed class BluetoothPowerSessionSupervisor`.

- [ ] **Step 1: Write failing serialization tests**

```csharp
[TestMethod]
public async Task DeliverPowerOffAndCloseAsync_WaitsForCurrentOperationAndWritesOnce()
{
    // Hold a fake normal operation, request final delivery, release it.
    // Assert no overlapping call, one final write, and Closing -> Disconnected.
}

[TestMethod]
public async Task CloseReconnectGate_PreventsConnectDuringShutdown()
{
    // Start Faulted, close the gate, ask for an operation, assert factory.ConnectAsync was not called.
}
```

- [ ] **Step 2: Run the test class and confirm it fails**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --filter FullyQualifiedName~BluetoothPowerSessionSupervisorTests`

Expected: FAIL because the supervisor does not exist.

- [ ] **Step 3: Implement `BluetoothPowerSessionSupervisor`**

Use one `SemaphoreSlim` to serialize all normal `IXb31Client` methods and final delivery. Before shutdown it may obtain/repair a session through the factory. After `CloseReconnectGate`, it may only use the already-owned session. A final delivery always closes the session; stale write failure becomes `Faulted`.

- [ ] **Step 4: Add unavailable-session coverage**

Add a fake factory that returns `Unavailable`; assert no usable session is retained and state becomes `Unavailable`.

- [ ] **Step 5: Run focused verification and commit**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --configuration Release --filter FullyQualifiedName~BluetoothPowerSessionSupervisorTests`

Expected: PASS.

```powershell
git add src/Xb31.Control/Shutdown src/Xb31.Control/OfflineStartupClient.cs tests/Xb31.Control.Tests/BluetoothPowerSessionSupervisorTests.cs
git commit -m "feat: supervise persistent XB31 session"
```

### Task 3: Build the shutdown coordinator and delivery lease

**Files:**
- Create: `src/Xb31.Control/Shutdown/ShutdownCoordinator.cs`
- Test: `tests/Xb31.Control.Tests/ShutdownCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IBluetoothPowerSessionSupervisor`, `BluetoothPowerSessionState`, `Xb31Result`.
- Produces:
  - `internal enum ShutdownGateDecision { Allow, CancelForDelivery, CancelUnavailableSession }`
  - `internal sealed class ShutdownCoordinator` with `ShutdownGateDecision BeginOrInspect()`, `bool TryConsumeDeliveryLease()`, and `Task CurrentDelivery { get; }`.
  - Constructor dependencies: supervisor, delivery timeout, lease lifetime, `Func<DateTimeOffset> now`, and trace callback.

- [ ] **Step 1: Write failing state/lease tests**

```csharp
[TestMethod]
public async Task LiveSession_BeginsOneDeliveryAndCreatesOneLeaseOnSuccess()
{
    // Call BeginOrInspect twice before completing a fake success.
    // Assert one delivery, then first lease consume true and second false.
}

[TestMethod]
public async Task FailedFinalWrite_CreatesNoLeaseAndDoesNotReconnect()
{
    // Complete WriteFailed; assert CancelUnavailableSession and reconnect gate stays closed.
}
```

Add a lease-expiry test using an injected clock at exactly 60 seconds.

- [ ] **Step 2: Run the coordinator tests and confirm they fail**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --filter FullyQualifiedName~ShutdownCoordinatorTests`

Expected: FAIL because the coordinator is absent.

- [ ] **Step 3: Implement `ShutdownCoordinator`**

Close the reconnect gate before queueing delivery. Queue exactly one worker with a linked cancellation source and the injected deadline. On `Success` or `CleanupFailed`, create one in-memory 60-second lease; any other result leaves a paused failure and creates no lease. The synchronous methods must never await delivery.

- [ ] **Step 4: Run focused verification and commit**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --configuration Release --filter FullyQualifiedName~ShutdownCoordinatorTests`

Expected: PASS.

```powershell
git add src/Xb31.Control/Shutdown/ShutdownCoordinator.cs tests/Xb31.Control.Tests/ShutdownCoordinatorTests.cs
git commit -m "feat: coordinate XB31 shutdown delivery"
```

### Task 4: Replace the synchronous Windows shutdown handler

**Files:**
- Modify: `src/Xb31.Control/SystemShutdownPowerOff.cs`
- Modify: `src/Xb31.Control/Display/DisplayStateMonitor.cs`
- Modify: `src/Xb31.Control/TrayApplicationController.cs`
- Test: `tests/Xb31.Control.Tests/SystemShutdownPowerOffTests.cs`

**Interfaces:**
- Consumes: `ShutdownCoordinator`, `ShutdownGateDecision`, `IShutdownBlockReason`.
- Produces: `internal bool SystemShutdownPowerOff.HandleMessage(...)`; `true` means `DisplayStateMonitor.WindowProc` must set `handled = true` and return zero to cancel the query.

- [ ] **Step 1: Replace old synchronous assertions with failing gate tests**

```csharp
[TestMethod]
public void LiveSession_QueryCreatesBlockReasonQueuesDeliveryAndReturnsCancelImmediately()
{
    var coordinator = new BlockingFakeCoordinator(ShutdownGateDecision.CancelForDelivery);
    bool handled = handler.HandleMessage(Window, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);

    Assert.IsTrue(handled);
    Assert.AreEqual(1, blockReason.CreateCalls);
    Assert.AreEqual(0, coordinator.WaitCalls);
}

[TestMethod]
public void DeliveryLease_QueryAllowsShutdownWithoutBluetoothWork()
{
    // Consume a fake lease; assert handled == false and no reason creation.
}
```

- [ ] **Step 2: Run the gate tests and confirm they fail**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --filter FullyQualifiedName~SystemShutdownPowerOffTests`

Expected: FAIL because the current handler waits on `IXb31Client.PowerOffAsync`.

- [ ] **Step 3: Implement the thin query gate**

Remove `Task.Run`, `Task.Wait`, `Thread.Sleep`, `CancellationTokenSource`, and `DisposeWhenFinished` from `SystemShutdownPowerOff`. On normal query, consume a lease or create/retain the reason, signal the coordinator, and return the cancel decision. Keep critical/logoff/Restart Manager requests as immediate allow. Make `WM_ENDSESSION` and WPF `Application.SessionEnding` diagnostic-only; remove its late power-off fallback.

- [ ] **Step 4: Wire the hook and block reason lifecycle**

In `DisplayStateMonitor.WindowProc`, set `handled = true` only for a cancel decision. Keep the reason registered during delivery or paused failure; release it after success cleanup, application exit, or explicit recovery. Marshal reason creation/destruction to the window-owning dispatcher.

- [ ] **Step 5: Add the regression cases**

Add data rows for logoff, `ENDSESSION_CLOSEAPP`, and critical shutdown that prove no coordinator action. Add a query-plus-`SessionEnding` test proving one delivery. Add `Disconnected`/ `Unavailable` tests proving immediate allow.

- [ ] **Step 6: Run focused verification and commit**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --configuration Release --filter FullyQualifiedName~SystemShutdownPowerOffTests`

Expected: PASS.

```powershell
git add src/Xb31.Control/SystemShutdownPowerOff.cs src/Xb31.Control/Display/DisplayStateMonitor.cs src/Xb31.Control/TrayApplicationController.cs tests/Xb31.Control.Tests/SystemShutdownPowerOffTests.cs
git commit -m "fix: gate XB31 delivery before Windows shutdown"
```

### Task 5: Wire the opt-in canary and app composition

**Files:**
- Modify: `src/Xb31.Control/App.xaml.cs`
- Modify: `src/Xb31.Control/MainWindow.xaml`, `src/Xb31.Control/MainWindow.xaml.cs`
- Modify: `src/Xb31.Control/TrayApplicationController.cs`, `src/Xb31.Control/Settings/UserSettings.cs`
- Test: `tests/Xb31.Control.Tests/UserSettingsTests.cs`, `tests/Xb31.Control.Tests/TrayApplicationControllerTests.cs`, `tests/Xb31.Control.Markup.Tests.ps1`

**Interfaces:**
- Consumes: supervisor, coordinator, and gate.
- Produces: `UserSettings.ReliableShutdownCanaryEnabled`, stored as DWORD `ReliableShutdownCanaryEnabled`, default `false`; a checkbox named `ReliableShutdownCanaryCheckBox` with content `Use reliable XB31 shutdown (canary)`. When it is false, the app permits shutdown without speaker I/O rather than using the old synchronous handler.

- [ ] **Step 1: Write failing opt-in tests**

```csharp
[TestMethod]
public void ReliableShutdownCanaryEnabled_MissingValueDefaultsOffAndChangesPersist()
{
    // Follow the existing SyncLightingWithDisplay registry fixture.
}
```

Add a PowerShell markup assertion for the named checkbox and checked/unchecked handlers.

- [ ] **Step 2: Run the new tests and confirm they fail**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --filter FullyQualifiedName~UserSettingsTests; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Xb31.Control.Markup.Tests.ps1`

Expected: FAIL because the setting and checkbox do not exist.

- [ ] **Step 3: Implement setting, control, and composition**

Follow the existing automation-settings event pattern. When disabled, retain the existing one-shot client for ordinary UI operations but configure the shutdown gate to allow without speaker I/O; do not instantiate persistent shutdown components or the legacy synchronous shutdown handler. When enabled, create the factory/supervisor/coordinator/gate and pass the supervisor as the window's `IXb31Client`, so normal UI commands share its serialized session.

- [ ] **Step 4: Add lifecycle tests**

Add `CanaryDisabled_DoesNotCreatePersistentShutdownComponentsOrStartShutdownBluetoothWork` and `CanaryEnabled_DisposesSupervisorAfterTrayControllerStops`.

- [ ] **Step 5: Run control verification and commit**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --configuration Release; powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Xb31.Control.Markup.Tests.ps1`

Expected: PASS.

```powershell
git add src/Xb31.Control tests/Xb31.Control.Tests tests/Xb31.Control.Markup.Tests.ps1
git commit -m "feat: add reliable shutdown canary"
```

### Task 6: Add asynchronous diagnostics and canary documentation

**Files:**
- Modify: `src/Xb31.Control/ShutdownTrace.cs`, `src/Xb31.Control/SystemShutdownPowerOff.cs`, `README.md`
- Test: `tests/Xb31.Control.Tests/SystemShutdownPowerOffTests.cs`, `tests/Xb31.Control.Tests/ShutdownCoordinatorTests.cs`

**Interfaces:**
- Consumes: gate decisions and coordinator transitions.
- Produces: `ShutdownTraceEntry` containing attempt ID, component, state/decision, outcome, and elapsed milliseconds.

- [ ] **Step 1: Write failing correlated-trace tests**

```csharp
[TestMethod]
public async Task SuccessfulDelivery_RecordsOneAttemptIdAcrossGateAndCoordinator()
{
    // Capture entries; assert shared nonempty attempt ID, success outcome, and no raw frame text.
}
```

- [ ] **Step 2: Run the trace tests and confirm they fail**

Run: `dotnet test .\tests\Xb31.Control.Tests\Xb31.Control.Tests.csproj --filter "FullyQualifiedName~SystemShutdownPowerOffTests|FullyQualifiedName~ShutdownCoordinatorTests"`

Expected: FAIL because current entries have no structured correlation.

- [ ] **Step 3: Implement non-blocking trace records**

Queue records to one background writer; the query handler may only enqueue. Preserve bounded rotation and disable-on-I/O-failure behavior. Record gate decision, supervisor state, write/cleanup start/end, lease create/consume, and block-reason API outcomes. Do not record sensitive Bluetooth/device data.

- [ ] **Step 4: Document the canary**

Add the opt-in steps, log location, manual test matrix, and promotion rule: several days with no app-associated `Kernel-Power 41`, `EventLog 6008`, Bluetooth driver error, or manual Bluetooth recovery. Do not call it production-ready.

- [ ] **Step 5: Run the complete offline gate and commit**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-All.ps1`

Expected: `PASS: complete XB31 offline and DryRun verification`.

```powershell
git add src/Xb31.Control/ShutdownTrace.cs src/Xb31.Control/SystemShutdownPowerOff.cs README.md tests/Xb31.Control.Tests
git commit -m "feat: trace reliable shutdown canary"
```

### Task 7: Run the manual canary gate

**Files:**
- Evidence: `%LOCALAPPDATA%\XB31 Control\shutdown.log` and Windows System Event Log.
- Modify: `README.md` only if a tested operational instruction needs correction.

**Interfaces:**
- Consumes: the opt-in implementation and diagnostics from Tasks 1-6.
- Produces: a dated manual-test record in the handoff/PR description; no default-enable code change.

- [ ] **Step 1: Run the session-only soak**

Keep shutdown interception disabled, maintain the session through normal use for several days, and record connection/recovery and auto-standby behavior.

- [ ] **Step 2: Exercise manual persistent-session power-off**

Verify delivery through the maintained session, immediate Bluetooth usability afterward, and reconnect after the speaker is manually powered on.

- [ ] **Step 3: Enable the gate and run the shutdown matrix**

Run normal shutdown, restart, speaker unavailable, Bluetooth disabled, stale-session write failure, critical-shutdown exclusion, and immediate retry. Do not force-shutdown the machine as part of a passing test.

- [ ] **Step 4: Review evidence**

For every normal case, verify one trace attempt ID and clean Windows shutdown events. Treat `Kernel-Power 41`, `EventLog 6008`, a BTHUSB error, unexpected driver reset, or manual Bluetooth recovery as a canary failure requiring diagnosis.

- [ ] **Step 5: Promote in a separate follow-up**

Only after the agreed several-day canary passes, make a separate reviewed change that defaults `ReliableShutdownCanaryEnabled` to `true`.

## Plan self-review

- **Spec coverage:** Tasks 1-2 provide the owned persistent session and reconnect gate; Tasks 3-4 provide cancellation, lease, Windows reason, exclusions, and no query-time I/O; Task 5 adds opt-in wiring; Task 6 adds diagnostics; Task 7 performs the staged canary.
- **Step scan:** Every implementation unit starts from a failing test, names contracts/files, runs focused verification, and ends with a reviewable commit.
- **Type consistency:** Factory creates session; supervisor owns factory/session; coordinator consumes supervisor; Windows gate consumes coordinator.
- **Review focus coverage:** stale socket and lease cases are Task 3; duplicate/exclusion cases are Task 4; opt-in configuration is Task 5.
- **Proportion:** The plan fixes interfaces, evidence, and ownership while leaving normal implementation details to the executor.
